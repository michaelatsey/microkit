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
/// <see cref="OutboxProcessorOptions.MaxPollingInterval"/>; a partial batch returns to
/// <see cref="OutboxProcessorOptions.PollingInterval"/>. A down endpoint is not the loop's to back
/// off (#188): the processor defers its rows to the end of the endpoint's own back-off
/// (<see cref="OutboxEndpointBackoff"/>), so the next claim skips them and the other endpoint's rows
/// keep this cadence. One exception, before saturation: a batch that carried rows handed over in
/// back-off (<see cref="OutboxBatchResult.HandedOverInBackOff"/> — a store that breaks the claim's
/// contract by not honouring its endpoint filter) waits <see cref="OutboxProcessorOptions.PollingInterval"/>,
/// or the loop would rewrite the down endpoint's backlog back to back. Such a store still costs a
/// down endpoint's backlog a rewrite each back-off period, and holds the other endpoint to one batch
/// per polling interval while it keeps handing that backlog over. A processor that
/// cannot be activated grows the delay toward
/// <see cref="OutboxProcessorOptions.TransportUnavailableBackoff"/>.
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

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled: only a host stop ends it.</summary>
    /// <param name="stoppingToken">The host's token.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    public Task RunAsync(CancellationToken stoppingToken)
    {
        var delay = options.PollingInterval;

        return GuardedLoop.RunAsync(
            async ct =>
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    IOutboxProcessor processor;
                    try
                    {
                        processor = scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, ct))
                    {
                        delay = Grow(delay, options.TransportUnavailableBackoff);
                        var activationDelay = delay;
                        GuardedLoop.Log(() => StoreEngineLogs.ActivationFailed(
                            logger, ex, Loop, store.Value, nameof(IOutboxProcessor), activationDelay));
                        return delay;
                    }

                    try
                    {
                        var result = await processor.ProcessBatchAsync(options.BatchSize, ct).ConfigureAwait(false);
                        delay = NextDelay(result, delay);
                        return delay;
                    }
                    catch (OutboxConfigurationException ex)
                    {
                        // The batch was settled — the rest of it released — before the processor
                        // rethrew. A missing registration does not fix itself, but only a host stop
                        // ends a loop: it backs off on the idle curve and reports it each time.
                        delay = Grow(delay, options.MaxPollingInterval);
                        var faultDelay = delay;
                        GuardedLoop.Log(() => StoreEngineLogs.ConfigurationFault(logger, ex, Loop, store.Value, faultDelay));
                        return delay;
                    }
                }
            },
            ex =>
            {
                // The cycle failed — the claim itself, most often: this store's database is
                // unreachable. Back off on the idle curve rather than polling at full speed.
                delay = Grow(delay, options.MaxPollingInterval);
                var failureDelay = delay;
                GuardedLoop.Log(() => StoreEngineLogs.LoopFailed(logger, ex, Loop, store.Value, failureDelay));
                return delay;
            },
            TimeProvider.System,
            stoppingToken);
    }

    /// <summary>The delay before the next batch, from the result of this one.</summary>
    /// <param name="result">The batch result.</param>
    /// <param name="current">The current delay.</param>
    /// <returns>The next delay.</returns>
    internal TimeSpan NextDelay(OutboxBatchResult result, TimeSpan current) => result switch
    {
        // No outage arm: a down endpoint's rows were deferred by the processor (OutboxEndpointBackoff),
        // so a batch that hit one is sized like any other — a full one cannot re-claim its own rows.
        { AbortReason: OutboxBatchAbortReason.Cancelled } => current,
        // Rows handed over while their endpoint is in back-off — a store that does not honour the
        // claim's endpoint filter, against its contract: polling again at once, even after a batch that also delivered something,
        // would rewrite the down endpoint's whole backlog a batch at a time.
        { HandedOverInBackOff: true } => options.PollingInterval,
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
