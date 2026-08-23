namespace MicroKit.Messaging.Processing;

/// <summary>
/// Hosted background service that deletes processed inbox rows once they are older than
/// <see cref="InboxProcessorOptions.RetentionDays"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without it the inbox grows without bound: <c>DeleteProcessedAsync</c> shipped with no caller
/// and <c>RetentionDays</c> did not exist, so every handled message stayed in the table for the
/// life of the deployment.
/// </para>
/// <para>
/// <b>Inbox retention is not housekeeping, and this worker is not the outbox's.</b> The table
/// only deduplicates messages it still holds, so deleting a row before its message can still be
/// redelivered reopens the door to reprocessing. That is why the default window is 30 days
/// rather than the outbox's 7, and why the two numbers must not be harmonised — see the remarks
/// on <see cref="InboxProcessorOptions.RetentionDays"/>.
/// </para>
/// <para>
/// Deliberately minimal — one pass on a slow timer, no admin surface, no per-tenant logic. It
/// deletes across every tenant (<c>tenantId: null</c>), which is also the only value that reaches
/// rows in a single-tenant deployment, where <see cref="InboxMessage.TenantId"/> is itself null.
/// </para>
/// <para>
/// <b>DI rule.</b> Only <see cref="IServiceScopeFactory"/> is injected. Scoped services are
/// resolved from the per-iteration scope.
/// </para>
/// </remarks>
internal sealed class InboxRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InboxProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InboxRetentionWorker> _logger;

    /// <summary>Initializes a new <see cref="InboxRetentionWorker"/>.</summary>
    /// <param name="scopeFactory">Creates the per-iteration scope.</param>
    /// <param name="options">Supplies the retention window and cadence.</param>
    /// <param name="timeProvider">Clock used to compute the cutoff.</param>
    /// <param name="logger">Logger.</param>
    public InboxRetentionWorker(
        IServiceScopeFactory scopeFactory,
        InboxProcessorOptions options,
        TimeProvider timeProvider,
        ILogger<InboxRetentionWorker> logger)
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
            // Disabled, not broken. Said once and loudly enough to be found later: an
            // unbounded table is a real cost, even though on the inbox it is the safe
            // direction to fail in.
            InboxProcessorLogs.RetentionDisabled(_logger, _options.RetentionDays);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                IInboxRetentionStore store;
                try
                {
                    store = scope.ServiceProvider.GetRequiredService<IInboxRetentionStore>();
                }
                catch (InvalidOperationException ex)
                {
                    InboxProcessorLogs.RetentionStoreUnresolvable(_logger, ex);
                    return;
                }

                var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
                var deleted = await store
                    .DeleteProcessedAsync(cutoff, tenantId: null, stoppingToken)
                    .ConfigureAwait(false);

                InboxProcessorLogs.RetentionPass(_logger, deleted, cutoff);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Housekeeping must never take the host down: the next pass retries.
                InboxProcessorLogs.RetentionFailed(_logger, ex, _options.RetentionInterval);
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
