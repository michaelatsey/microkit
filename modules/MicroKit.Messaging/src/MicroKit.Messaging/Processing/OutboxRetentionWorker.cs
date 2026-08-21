namespace MicroKit.Messaging.Processing;

/// <summary>
/// Hosted background service that deletes published outbox rows once they are older than
/// <see cref="OutboxProcessorOptions.RetentionDays"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without it the outbox grows without bound: <c>DeleteProcessedAsync</c> shipped with no caller
/// and <c>RetentionDays</c> was read by nothing, so every successfully dispatched message stayed
/// in the table for the life of the deployment.
/// </para>
/// <para>
/// Deliberately minimal — one pass on a slow timer, no admin surface, no per-tenant logic. It
/// deletes across every tenant (<c>tenantId: null</c>), which is also the only value that reaches
/// rows in a single-tenant deployment, where <see cref="OutboxMessage.TenantId"/> is itself null.
/// </para>
/// <para>
/// <b>DI rule.</b> Only <see cref="IServiceScopeFactory"/> is injected. Scoped services are
/// resolved from the per-iteration scope.
/// </para>
/// </remarks>
internal sealed class OutboxRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxRetentionWorker> _logger;

    /// <summary>Initializes a new <see cref="OutboxRetentionWorker"/>.</summary>
    /// <param name="scopeFactory">Creates the per-iteration scope.</param>
    /// <param name="options">Supplies the retention window and cadence.</param>
    /// <param name="timeProvider">Clock used to compute the cutoff.</param>
    /// <param name="logger">Logger.</param>
    public OutboxRetentionWorker(
        IServiceScopeFactory scopeFactory,
        OutboxProcessorOptions options,
        TimeProvider timeProvider,
        ILogger<OutboxRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            // Disabled, not broken. Said once and loudly enough to be found later, because a
            // silently unbounded table is the failure this worker exists to prevent.
            OutboxProcessorLogs.RetentionDisabled(_logger, _options.RetentionDays);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                IOutboxRetentionStore store;
                try
                {
                    store = scope.ServiceProvider.GetRequiredService<IOutboxRetentionStore>();
                }
                catch (InvalidOperationException ex)
                {
                    OutboxProcessorLogs.RetentionStoreUnresolvable(_logger, ex);
                    return;
                }

                var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
                var deleted = await store
                    .DeleteProcessedAsync(cutoff, tenantId: null, stoppingToken)
                    .ConfigureAwait(false);

                OutboxProcessorLogs.RetentionPass(_logger, deleted, cutoff);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Housekeeping must never take the host down: the next pass retries.
                OutboxProcessorLogs.RetentionFailed(_logger, ex, _options.RetentionInterval);
            }

            try
            {
                await Task.Delay(_options.RetentionInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
