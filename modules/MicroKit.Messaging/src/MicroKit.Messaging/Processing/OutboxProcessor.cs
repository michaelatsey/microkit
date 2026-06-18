using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Topology-agnostic outbox batch engine. Drains pending <see cref="OutboxMessage"/> rows,
/// acquires an optimistic lease per message, dispatches via <see cref="IOutboxDispatcher"/>,
/// then marks each message as <c>Published</c>, retried, or dead-lettered.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="IExecutionScope"/> is created per message — never shared across a batch.
/// This guarantees that a <c>DbContext</c> exception on message N cannot corrupt the state
/// for message N+1, and that <c>TenantId</c> context is fresh for each message.
/// </para>
/// <para>
/// Retry back-off follows <c>2^retryCount</c> seconds (capped at 3 600 s, approximately 1 h).
/// Dead-lettering occurs when <c>retryCount &gt;= MaxRetries</c>.
/// </para>
/// </remarks>
internal sealed class OutboxProcessor : IOutboxProcessor
{
    // Batch-scoped by design (ADR-MSG-002 Shared-DB cross-tenant reservation): NOT resolved from
    // the per-message IExecutionScope. Per-tenant DB resolution is the deferred
    // PerTenantOutboxCoordinator's responsibility, not the processor's.
    private readonly IOutboxProcessorStore _store;
    private readonly IExecutionScopeFactory _executionScopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    /// <summary>
    /// Initializes a new <see cref="OutboxProcessor"/>.
    /// </summary>
    public OutboxProcessor(
        IOutboxProcessorStore store,
        IExecutionScopeFactory executionScopeFactory,
        OutboxProcessorOptions options,
        ILogger<OutboxProcessor> logger)
    {
        _store = store;
        _executionScopeFactory = executionScopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var messages = await _store.GetPendingAsync(batchSize, cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            var ctx = new MessageCtx
            {
                TenantId = message.TenantId,
                CorrelationId = message.CorrelationId?.Value.ToString(),
                CausationId = message.CausationId?.Value.ToString(),
            };

            await using var scope = await _executionScopeFactory
                .CreateScopeAsync(ctx, cancellationToken)
                .ConfigureAwait(false);

            var lockExpiry = DateTimeOffset.UtcNow.Add(_options.LockDuration);
            var acquired = await _store
                .AcquireLeaseAsync(message.Id, lockExpiry, cancellationToken)
                .ConfigureAwait(false);

            if (!acquired)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Outbox lease not acquired for message {MessageId} — skipping (another processor won).",
                        message.Id.Value);
                }
                continue;
            }

            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

            try
            {
                await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                await _store.MarkPublishedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var nextRetryCount = message.RetryCount + 1;

                if (nextRetryCount >= _options.MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "Outbox message {MessageId} ({EventType}) exceeded max retries ({MaxRetries}). Dead-lettering.",
                        message.Id.Value,
                        message.EventType,
                        _options.MaxRetries);

                    await _store.DeadLetterAsync(message.Id, ex.Message, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Outbox message {MessageId} ({EventType}) failed dispatch (attempt {Attempt}/{MaxRetries}). Will retry.",
                        message.Id.Value,
                        message.EventType,
                        nextRetryCount,
                        _options.MaxRetries);

                    await _store.MarkFailedAsync(message.Id, ex.Message, nextRetryCount, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
