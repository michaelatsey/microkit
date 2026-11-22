namespace MicroKit.Messaging.Processing;


/// <summary>
/// The inbox loop of one store, or of one instance of a partitioned store (#191): claims the rows due
/// for an attempt, handles them, and adapts its cadence to the result. Run by
/// <see cref="MessageStoreEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its back-off is its own.</b> Each store has its own loop and its own delay, so a store whose
/// database is unreachable backs off alone and never delays another store.
/// </para>
/// <para>
/// <b>Cadence.</b> A saturated batch polls again at once; an empty queue grows the delay toward
/// <see cref="InboxProcessorOptions.MaxPollingInterval"/>; an unavailable dependency grows it toward
/// <see cref="InboxProcessorOptions.DependencyUnavailableBackoff"/>; a partial batch returns to
/// <see cref="InboxProcessorOptions.PollingInterval"/>.
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
/// <b>An instance's loop (#191)</b> behaves as <see cref="OutboxLoop"/>'s: its scope prepared for its
/// instance, the engine's <see cref="FifoGate"/> held from claim to settlement, its own events, and an
/// end between batches once the instance is removed.
/// </para>
/// </remarks>
internal sealed class InboxLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    InboxProcessorOptions options,
    ILogger<InboxLoop> logger,
    StoreInstanceState? instance = null)
{
    private const string Loop = "inbox";

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
                            delay = Grow(delay, options.DependencyUnavailableBackoff);
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

                    IInboxProcessor processor;
                    try
                    {
                        processor = scope.ServiceProvider.GetRequiredKeyedService<IInboxProcessor>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, ct))
                    {
                        delay = Grow(delay, options.DependencyUnavailableBackoff);
                        var activationDelay = delay;
                        GuardedLoop.Log(() => ActivationFailed(ex, activationDelay));
                        return delay;
                    }

                    if (instance is not null && !await OutboxLoop.EnterAsync(instance, ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    try
                    {
                        var result = await processor.ProcessBatchAsync(options.BatchSize, ct).ConfigureAwait(false);
                        delay = NextDelay(result, delay);
                        return delay;
                    }
                    catch (InboxConfigurationException ex)
                    {
                        // The batch was settled — every row released — before the processor rethrew.
                        // Only a host stop ends a loop: it backs off on the idle curve, reported each time.
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
            TimeProvider.System,
            stoppingToken,
            instance?.Stopping ?? default);
    }

    private void ActivationFailed(Exception ex, TimeSpan delay)
    {
        if (instance is null)
        {
            StoreEngineLogs.ActivationFailed(logger, ex, Loop, store.Value, nameof(IInboxProcessor), delay);
        }
        else
        {
            StoreInstanceLogs.ActivationFailed(logger, ex, Loop, store.Value, instance.Instance.Id, nameof(IInboxProcessor), delay);
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
    internal TimeSpan NextDelay(InboxBatchResult result, TimeSpan current) => result switch
    {
        { AbortReason: InboxBatchAbortReason.DependencyUnavailable }
            => Grow(current, options.DependencyUnavailableBackoff),
        { AbortReason: InboxBatchAbortReason.Cancelled } => current,
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
