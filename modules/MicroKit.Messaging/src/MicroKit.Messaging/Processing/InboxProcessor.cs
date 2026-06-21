using MessageCtx = MicroKit.Messaging.Execution.ExecutionContext;
using MicroKit.Messaging.Registry;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Topology-agnostic inbox drain engine. Retrieves pending <see cref="InboxMessage"/> rows,
/// acquires a processing lease, deserializes, resolves the registered handler from DI,
/// invokes it, then marks each message as <c>Processed</c>, retried, or dead-lettered.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure drain loop — it never calls <c>ExistsAsync</c> or <c>AddAsync</c>.
/// Ingestion is performed by <c>InProcessMessagePublisher</c> (or a broker adapter).
/// </para>
/// <para>
/// One <see cref="IExecutionScope"/> is created per message — never shared across a batch.
/// Failure policy is symmetric with <see cref="OutboxProcessor"/>: exponential back-off
/// <c>2^retryCount</c> seconds (capped at 3 600 s), dead-letter after <c>MaxRetries</c>.
/// See ADR-MSG-003.
/// </para>
/// </remarks>
internal sealed class InboxProcessor : IInboxProcessor
{
    // Batch-scoped by design (ADR-MSG-002 Shared-DB cross-tenant reservation): NOT resolved from
    // the per-message IExecutionScope. Per-tenant DB resolution is the deferred
    // PerTenantInboxCoordinator's responsibility, not the processor's.
    private readonly IInboxStore _store;
    private readonly MessageHandlerRegistry _registry;
    private readonly IMessageSerializer _serializer;
    private readonly IExecutionScopeFactory _executionScopeFactory;
    private readonly InboxProcessorOptions _options;
    private readonly ILogger<InboxProcessor> _logger;

    /// <summary>
    /// Initializes a new <see cref="InboxProcessor"/>.
    /// </summary>
    public InboxProcessor(
        IInboxStore store,
        MessageHandlerRegistry registry,
        IMessageSerializer serializer,
        IExecutionScopeFactory executionScopeFactory,
        InboxProcessorOptions options,
        ILogger<InboxProcessor> logger)
    {
        _store = store;
        _registry = registry;
        _serializer = serializer;
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
            // 1 — registry lookup (before acquiring a lease — no point holding one if unknown)
            if (!_registry.TryGetInvoker(message.ConsumerType, out var entry))
            {
                _logger.LogWarning(
                    "Inbox message {MessageId} references unknown consumer type '{ConsumerType}'. " +
                    "Ensure AddMessageHandler<THandler, TEvent>() is called at startup.",
                    message.MessageId.Value,
                    message.ConsumerType);

                await ApplyFailurePolicyAsync(
                    message, "Unknown consumer type: " + message.ConsumerType, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var ctx = new MessageCtx
            {
                TenantId = message.TenantId,
                CorrelationId = message.CorrelationId?.Value.ToString(),
                CausationId = message.CausationId?.Value.ToString(),
            };

            await using var scope = await _executionScopeFactory
                .CreateScopeAsync(ctx, cancellationToken)
                .ConfigureAwait(false);

            // 2 — acquire lease
            var lockUntil = DateTimeOffset.UtcNow.Add(_options.LeaseDuration);
            await _store.MarkProcessingAsync(message.MessageId, message.ConsumerType, lockUntil, cancellationToken)
                .ConfigureAwait(false);

            // 3 — deserialize and re-narrow to IIntegrationEvent (the inbox only ever holds
            // integration events; domain-event notifications never enter the inbox path).
            if (_serializer.Deserialize(message.Payload, message.EventType) is not IIntegrationEvent evt)
            {
                _logger.LogError(
                    "Inbox message {MessageId}: deserialization returned null or a non-IIntegrationEvent " +
                    "payload for EventType '{EventType}'. Verify the event type is resolvable in the " +
                    "current assembly context and implements IIntegrationEvent.",
                    message.MessageId.Value,
                    message.EventType);

                await ApplyFailurePolicyAsync(
                    message, "Deserialization failed for EventType: " + message.EventType, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            // 4 — resolve handler and invoke (ADR-MSG-003 symmetric retry/dead-letter)
            var handler = scope.ServiceProvider.GetRequiredService(entry.HandlerType);
            try
            {
                await entry.Invoker(handler, evt, cancellationToken).ConfigureAwait(false);
                await _store.MarkProcessedAsync(message.MessageId, message.ConsumerType, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Inbox message {MessageId} (consumer: {ConsumerType}) handler threw an exception.",
                    message.MessageId.Value,
                    message.ConsumerType);

                await ApplyFailurePolicyAsync(message, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ApplyFailurePolicyAsync(
        InboxMessage message, string reason, CancellationToken ct)
    {
        var nextRetryCount = message.RetryCount + 1;

        if (nextRetryCount >= _options.MaxRetries)
        {
            _logger.LogError(
                "Inbox message {MessageId} (consumer: {ConsumerType}) exceeded max retries ({MaxRetries}). Dead-lettering. Reason: {Reason}",
                message.MessageId.Value,
                message.ConsumerType,
                _options.MaxRetries,
                reason);

            await _store.DeadLetterAsync(message.MessageId, message.ConsumerType, reason, ct)
                .ConfigureAwait(false);
        }
        else
        {
            await _store.MarkFailedAsync(message.MessageId, message.ConsumerType, reason, nextRetryCount, ct)
                .ConfigureAwait(false);
        }
    }
}
