namespace MicroKit.Messaging.Processing;


/// <summary>
/// The outbox drain loop of one store: claims a batch, dispatches it, and adapts its cadence to the
/// result. One instance per store, run by <see cref="MessageStoreEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its back-off is its own.</b> Each store has its own loop and its own delay, so a store whose
/// database is unreachable backs off alone and never delays another store.
/// </para>
/// <para>
/// <b>Cadence.</b> A saturated batch polls again at once; an empty queue grows the delay toward
/// <see cref="OutboxProcessorOptions.MaxPollingInterval"/>; a transport outage or an abandoned
/// batch grows it toward <see cref="OutboxProcessorOptions.TransportUnavailableBackoff"/>; a partial
/// batch returns to <see cref="OutboxProcessorOptions.PollingInterval"/>.
/// </para>
/// <para>
/// <b>Activation is not registration.</b> The processor and its store are registered — host start
/// rejects a store missing one — so an exception while resolving them is an activation failure:
/// logged as such (event 1400), with back-off, and the loop keeps running.
/// </para>
/// <para>
/// <b>Shutdown is not a failure, and only shutdown ends the loop.</b> It ends once the host's token
/// is cancelled (<see cref="HostStop"/>); the processor releases what it claimed. Any exception
/// while the host runs — an <see cref="ObjectDisposedException"/> included — is a failed cycle,
/// logged and backed off.
/// </para>
/// </remarks>
internal sealed class OutboxLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    OutboxProcessorOptions options,
    ILogger<OutboxLoop> logger)
{
    private const string Loop = "outbox";

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled or a configuration fault stops it.</summary>
    /// <param name="stoppingToken">The host's token.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var delay = options.PollingInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    IOutboxProcessor? processor = null;
                    try
                    {
                        processor = scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, stoppingToken))
                    {
                        delay = Grow(delay, options.TransportUnavailableBackoff);
                        StoreEngineLogs.ActivationFailed(
                            logger, ex, Loop, store.Value, nameof(IOutboxProcessor), delay);
                    }

                    if (processor is not null)
                    {
                        var result = await processor.ProcessBatchAsync(options.BatchSize, stoppingToken)
                            .ConfigureAwait(false);
                        delay = NextDelay(result, delay);
                    }
                }
            }
            catch (OutboxConfigurationException ex)
            {
                // The batch was settled — every row released — before the processor rethrew. A
                // missing registration does not fix itself, so the loop stops rather than retrying
                // forever, and only this store's loop stops.
                StoreEngineLogs.LoopStopped(logger, ex, Loop, store.Value);
                return;
            }
            catch (Exception ex) when (HostStop.Is(ex, stoppingToken))
            {
                break;
            }
            catch (Exception ex)
            {
                // The claim itself failed: this store's database is unreachable. Back off on the
                // idle curve rather than polling at full speed.
                delay = Grow(delay, options.MaxPollingInterval);
                StoreEngineLogs.LoopFailed(logger, ex, Loop, store.Value, delay);
            }

            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>The delay before the next batch, from the result of this one.</summary>
    /// <param name="result">The batch result.</param>
    /// <param name="current">The current delay.</param>
    /// <returns>The next delay.</returns>
    internal TimeSpan NextDelay(OutboxBatchResult result, TimeSpan current) => result switch
    {
        // Checked BEFORE saturation: an abandoned batch still reports every row it claimed, so a
        // full one would otherwise poll again at once and spin against the database.
        { AbortReason: OutboxBatchAbortReason.TransportUnavailable
            or OutboxBatchAbortReason.DispatcherActivationFailed }
            => Grow(current, options.TransportUnavailableBackoff),
        { AbortReason: OutboxBatchAbortReason.Cancelled } => current,
        var r when r.IsSaturated(options.BatchSize) => TimeSpan.Zero,
        { HasWork: false } => Grow(current, options.MaxPollingInterval),
        _ => options.PollingInterval,
    };

    private TimeSpan Grow(TimeSpan current, TimeSpan ceiling)
    {
        var next = current <= TimeSpan.Zero ? options.PollingInterval : current * 2;
        return next > ceiling ? ceiling : next;
    }
}
