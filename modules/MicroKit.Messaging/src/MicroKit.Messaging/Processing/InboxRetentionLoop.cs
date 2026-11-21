namespace MicroKit.Messaging.Processing;


/// <summary>
/// The inbox retention loop of one store: deletes processed rows older than
/// <see cref="InboxProcessorOptions.RetentionDays"/>, every
/// <see cref="InboxProcessorOptions.RetentionInterval"/>, across every tenant.
/// </summary>
/// <remarks>
/// <para>
/// A retention of zero days or less disables it, said once at start. A failed pass — the store's
/// database unreachable, or its retention store failing to activate (event 1400) — is logged and
/// retried at the next interval; housekeeping never stops the host or another store.
/// </para>
/// </remarks>
internal sealed class InboxRetentionLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    InboxProcessorOptions options,
    TimeProvider timeProvider,
    ILogger<InboxRetentionLoop> logger)
{
    private const string Loop = "inbox retention";

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled.</summary>
    /// <param name="stoppingToken">The host's token.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    public Task RunAsync(CancellationToken stoppingToken)
    {
        if (options.RetentionDays <= 0)
        {
            GuardedLoop.Log(() => InboxProcessorLogs.RetentionDisabled(logger, options.RetentionDays));
            return Task.CompletedTask;
        }

        return GuardedLoop.RunAsync(
            async ct =>
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    IInboxRetentionStore retention;
                    try
                    {
                        retention = scope.ServiceProvider.GetRequiredKeyedService<IInboxRetentionStore>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, ct))
                    {
                        GuardedLoop.Log(() => StoreEngineLogs.ActivationFailed(
                            logger, ex, Loop, store.Value, nameof(IInboxRetentionStore), options.RetentionInterval));
                        return options.RetentionInterval;
                    }

                    var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
                    var deleted = await retention.DeleteProcessedAsync(cutoff, tenantId: null, ct).ConfigureAwait(false);
                    GuardedLoop.Log(() => InboxProcessorLogs.RetentionPass(logger, deleted, cutoff));
                    return options.RetentionInterval;
                }
            },
            ex =>
            {
                GuardedLoop.Log(() => InboxProcessorLogs.RetentionFailed(logger, ex, options.RetentionInterval));
                return options.RetentionInterval;
            },
            // The interval runs on real time, as it always has; the cutoff reads the injected clock.
            TimeProvider.System,
            stoppingToken);
    }
}
