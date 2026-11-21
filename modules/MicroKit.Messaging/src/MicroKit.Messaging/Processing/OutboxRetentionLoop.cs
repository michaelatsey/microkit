namespace MicroKit.Messaging.Processing;


/// <summary>
/// The outbox retention loop of one store: deletes processed rows older than
/// <see cref="OutboxProcessorOptions.RetentionDays"/>, every
/// <see cref="OutboxProcessorOptions.RetentionInterval"/>, across every tenant.
/// </summary>
/// <remarks>
/// <para>
/// A retention of zero days or less disables it, said once at start. A failed pass — the store's
/// database unreachable, or its retention store failing to activate (event 1400) — is logged and
/// retried at the next interval; housekeeping never stops the host or another store.
/// </para>
/// </remarks>
internal sealed class OutboxRetentionLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    OutboxProcessorOptions options,
    TimeProvider timeProvider,
    ILogger<OutboxRetentionLoop> logger)
{
    private const string Loop = "outbox retention";

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled.</summary>
    /// <param name="stoppingToken">The host's token.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    public Task RunAsync(CancellationToken stoppingToken)
    {
        if (options.RetentionDays <= 0)
        {
            GuardedLoop.Log(() => OutboxProcessorLogs.RetentionDisabled(logger, options.RetentionDays));
            return Task.CompletedTask;
        }

        return GuardedLoop.RunAsync(
            async ct =>
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    IOutboxRetentionStore retention;
                    try
                    {
                        retention = scope.ServiceProvider.GetRequiredKeyedService<IOutboxRetentionStore>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, ct))
                    {
                        GuardedLoop.Log(() => StoreEngineLogs.ActivationFailed(
                            logger, ex, Loop, store.Value, nameof(IOutboxRetentionStore), options.RetentionInterval));
                        return options.RetentionInterval;
                    }

                    var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
                    var deleted = await retention.DeleteProcessedAsync(cutoff, tenantId: null, ct).ConfigureAwait(false);
                    GuardedLoop.Log(() => OutboxProcessorLogs.RetentionPass(logger, deleted, cutoff));
                    return options.RetentionInterval;
                }
            },
            ex =>
            {
                GuardedLoop.Log(() => OutboxProcessorLogs.RetentionFailed(logger, ex, options.RetentionInterval));
                return options.RetentionInterval;
            },
            // The interval runs on real time, as it always has; the cutoff reads the injected clock.
            TimeProvider.System,
            stoppingToken);
    }
}
