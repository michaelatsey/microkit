namespace MicroKit.Messaging.Processing;


/// <summary>
/// The outbox drain loop of one store, or of one instance of a partitioned store (#191): claims a
/// batch, dispatches it, and adapts its cadence to the result. Run by <see cref="MessageStoreEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its back-off is its own.</b> Each store has its own loop and its own delay, so a store whose
/// database is unreachable backs off alone and never delays another store.
/// </para>
/// <para>
/// <b>Cadence.</b> A saturated batch polls again at once; an empty queue grows the delay toward
/// <see cref="OutboxProcessorOptions.MaxPollingInterval"/>; a partial batch returns to
/// <see cref="OutboxProcessorOptions.PollingInterval"/>; a commit cuts either of these two delays
/// (below). A down endpoint is not the loop's to back
/// off (#188): the processor defers its rows to the end of the endpoint's own back-off
/// (<see cref="OutboxEndpointBackoff"/>), so the next claim skips them and the other endpoint's rows
/// keep this cadence. One exception, before saturation: a batch that carried rows handed over in
/// back-off (<see cref="OutboxBatchResult.HandedOverInBackOff"/> — a store that breaks the claim's
/// contract by not honouring its endpoint or tenant filters) waits <see cref="OutboxProcessorOptions.PollingInterval"/>,
/// or the loop would rewrite the down endpoint's backlog back to back. Such a store still costs a
/// down endpoint's backlog a rewrite each back-off period, and holds the other endpoint to one batch
/// per polling interval while it keeps handing that backlog over. A processor that
/// cannot be activated grows the delay toward
/// <see cref="OutboxProcessorOptions.TransportUnavailableBackoff"/>.
/// </para>
/// <para>
/// <b>Woken by a commit (#215).</b> A store provider reports a commit through
/// <see cref="IOutboxCommitSignal"/>, which signals this loop's <see cref="LoopWake"/>. The signal is
/// drained right before each claim, so a commit landing during a batch ends the next wait at once. A
/// signal cuts only the delay after an empty or a partial batch, and starts a claim no sooner than
/// <see cref="OutboxProcessorOptions.PollingInterval"/> after the previous claim began, to the timer's
/// precision: during a
/// transport outage, commits would otherwise cost a claim each, each reading the down endpoint's
/// backlog. It never cuts the hand-over guard, a failure's, an activation's, a preparation's or a
/// configuration fault's back-off. An empty claim after a wake grows the delay from where it was.
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
/// <para>
/// <b>An instance's loop (#191)</b> prepares each cycle's scope for its instance before resolving
/// anything, holds the engine's <see cref="FifoGate"/> from its claim to its settlement, logs its own
/// events (1411–1414) inside a logging scope naming the store and the instance, and also ends once the
/// instance is removed — between batches: the batch in flight settles on the host's token.
/// </para>
/// </remarks>
internal sealed class OutboxLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    OutboxProcessorOptions options,
    ILogger<OutboxLoop> logger,
    StoreInstanceState? instance = null,
    LoopWake? wake = null)
{
    private const string Loop = "outbox";

    // The clock the loop waits on, and so the one its wake's floor is read on (#213 owns making it
    // the host's).
    private static readonly TimeProvider Clock = TimeProvider.System;

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
                    using var instanceScope = instance is null ? null : logger.BeginScope(instance.LogScope);
                    if (instance is not null)
                    {
                        bool ready;
                        try
                        {
                            ready = await instance.PrepareLoopScopeAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (!HostStop.Is(ex, ct))
                        {
                            delay = Grow(delay, options.TransportUnavailableBackoff);
                            var preparationDelay = delay;
                            GuardedLoop.Log(() => StoreInstanceLogs.PreparationFailed(
                                logger, ex, Loop, store.Value, instance.Instance.Id, preparationDelay));
                            return delay;
                        }

                        if (!ready)
                        {
                            // The store was refused at its check (1409): this instance does not start.
                            return null;
                        }
                    }

                    IOutboxProcessor processor;
                    try
                    {
                        processor = scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, ct))
                    {
                        delay = Grow(delay, options.TransportUnavailableBackoff);
                        var activationDelay = delay;
                        GuardedLoop.Log(() => ActivationFailed(ex, activationDelay));
                        return delay;
                    }

                    if (instance is not null && !await EnterAsync(instance, ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    try
                    {
                        // Drained right before the claim, after the gate (#215 D5): a commit signalled
                        // before this point is visible to the claim, one signalled during the batch
                        // stays pending and ends the next wait at once.
                        wake?.ClaimStarting();
                        var result = await processor.ProcessBatchAsync(options.BatchSize, ct).ConfigureAwait(false);
                        (delay, var wakeable) = Next(result, delay);
                        if (wakeable)
                        {
                            // Only the delay of an idle or a partial batch is cut by a commit (D6),
                            // no sooner than a polling interval after this claim began, to the timer's
                            // precision (D6b).
                            wake?.Arm(options.PollingInterval);
                        }

                        return delay;
                    }
                    catch (OutboxConfigurationException ex)
                    {
                        // The batch was settled — the rest of it released — before the processor
                        // rethrew. A missing registration does not fix itself, but only a host stop
                        // ends a loop: it backs off on the idle curve and reports it each time.
                        delay = Grow(delay, options.MaxPollingInterval);
                        var faultDelay = delay;
                        GuardedLoop.Log(() => ConfigurationFault(ex, faultDelay));
                        return delay;
                    }
                    finally
                    {
                        // Every path out of a batch returns its permit: a leaked one would stop every
                        // instance's loops.
                        instance?.Gate.Release();
                    }
                }
            },
            ex =>
            {
                // The cycle failed — the claim itself, most often: this store's database is
                // unreachable. Back off on the idle curve rather than polling at full speed.
                delay = Grow(delay, options.MaxPollingInterval);
                var failureDelay = delay;
                GuardedLoop.Log(() => LoopFailed(ex, failureDelay));
                return delay;
            },
            Clock,
            wake,
            stoppingToken,
            instance?.Stopping ?? default);
    }

    /// <summary>
    /// Waits for the engine's gate (#191 D6), behind every instance that asked first. False when the
    /// instance was removed meanwhile: the loop ends, having claimed nothing.
    /// </summary>
    internal static async ValueTask<bool> EnterAsync(StoreInstanceState instance, CancellationToken ct = default)
    {
        try
        {
            await instance.Gate.EnterAsync(instance.Stopping).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private void ActivationFailed(Exception ex, TimeSpan delay)
    {
        if (instance is null)
        {
            StoreEngineLogs.ActivationFailed(logger, ex, Loop, store.Value, nameof(IOutboxProcessor), delay);
        }
        else
        {
            StoreInstanceLogs.ActivationFailed(logger, ex, Loop, store.Value, instance.Instance.Id, nameof(IOutboxProcessor), delay);
        }
    }

    private void ConfigurationFault(Exception ex, TimeSpan delay)
    {
        if (instance is null)
        {
            StoreEngineLogs.ConfigurationFault(logger, ex, Loop, store.Value, delay);
        }
        else
        {
            StoreInstanceLogs.ConfigurationFault(logger, ex, Loop, store.Value, instance.Instance.Id, delay);
        }
    }

    private void LoopFailed(Exception ex, TimeSpan delay)
    {
        if (instance is null)
        {
            StoreEngineLogs.LoopFailed(logger, ex, Loop, store.Value, delay);
        }
        else
        {
            StoreInstanceLogs.LoopFailed(logger, ex, Loop, store.Value, instance.Instance.Id, delay);
        }
    }

    /// <summary>The delay before the next batch, from the result of this one.</summary>
    /// <param name="result">The batch result.</param>
    /// <param name="current">The current delay.</param>
    /// <returns>The next delay.</returns>
    internal TimeSpan NextDelay(OutboxBatchResult result, TimeSpan current) => Next(result, current).Delay;

    /// <summary>
    /// The delay before the next batch, and whether a commit may cut it (#215 D6): only the idle and
    /// the partial-batch delays may.
    /// </summary>
    /// <param name="result">The batch result.</param>
    /// <param name="current">The current delay.</param>
    /// <returns>The next delay, and whether it is wakeable.</returns>
    internal (TimeSpan Delay, bool Wakeable) Next(OutboxBatchResult result, TimeSpan current) => result switch
    {
        // No outage arm: a down endpoint's rows were deferred by the processor (OutboxEndpointBackoff),
        // so a batch that hit one is sized like any other — a full one cannot re-claim its own rows.
        { AbortReason: OutboxBatchAbortReason.Cancelled } => (current, false),
        // Rows handed over while their endpoint, or their tenant on it, is in back-off — a store that
        // does not honour the claim's endpoint or tenant filters, against its contract: polling again
        // at once, even after a batch that also delivered something, would rewrite the down
        // endpoint's or tenant's whole backlog a batch at a time. A commit does not cut this guard.
        { HandedOverInBackOff: true } => (options.PollingInterval, false),
        var r when r.IsSaturated(options.BatchSize) => (TimeSpan.Zero, false),
        { HasWork: false } => (Grow(current, options.MaxPollingInterval), true),
        _ => (options.PollingInterval, true),
    };

    private TimeSpan Grow(TimeSpan current, TimeSpan ceiling)
    {
        var next = current <= TimeSpan.Zero ? options.PollingInterval : current * 2;
        return next > ceiling ? ceiling : next;
    }
}
