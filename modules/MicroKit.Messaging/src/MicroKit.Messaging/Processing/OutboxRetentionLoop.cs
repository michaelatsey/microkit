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
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (options.RetentionDays <= 0)
        {
            OutboxProcessorLogs.RetentionDisabled(logger, options.RetentionDays);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    IOutboxRetentionStore? retention = null;
                    try
                    {
                        retention = scope.ServiceProvider.GetRequiredKeyedService<IOutboxRetentionStore>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, stoppingToken))
                    {
                        StoreEngineLogs.ActivationFailed(
                            logger, ex, Loop, store.Value, nameof(IOutboxRetentionStore), options.RetentionInterval);
                    }

                    if (retention is not null)
                    {
                        var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
                        var deleted = await retention
                            .DeleteProcessedAsync(cutoff, tenantId: null, stoppingToken)
                            .ConfigureAwait(false);

                        OutboxProcessorLogs.RetentionPass(logger, deleted, cutoff);
                    }
                }
            }
            catch (Exception ex) when (HostStop.Is(ex, stoppingToken))
            {
                break;
            }
            catch (Exception ex)
            {
                OutboxProcessorLogs.RetentionFailed(logger, ex, options.RetentionInterval);
            }

            try
            {
                await Task.Delay(options.RetentionInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
