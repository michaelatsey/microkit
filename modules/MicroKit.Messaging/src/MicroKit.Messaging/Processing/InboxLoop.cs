namespace MicroKit.Messaging.Processing;


/// <summary>
/// The inbox loop of one store: claims the rows due for an attempt, handles them, and adapts its
/// cadence to the result. One instance per store, run by <see cref="MessageStoreEngine"/>.
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
/// </remarks>
internal sealed class InboxLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    InboxProcessorOptions options,
    ILogger<InboxLoop> logger)
{
    private const string Loop = "inbox";

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
                    IInboxProcessor? processor = null;
                    try
                    {
                        processor = scope.ServiceProvider.GetRequiredKeyedService<IInboxProcessor>(store);
                    }
                    catch (Exception ex) when (!HostStop.Is(ex, stoppingToken))
                    {
                        delay = Grow(delay, options.DependencyUnavailableBackoff);
                        StoreEngineLogs.ActivationFailed(
                            logger, ex, Loop, store.Value, nameof(IInboxProcessor), delay);
                    }

                    if (processor is not null)
                    {
                        var result = await processor.ProcessBatchAsync(options.BatchSize, stoppingToken)
                            .ConfigureAwait(false);
                        delay = NextDelay(result, delay);
                    }
                }
            }
            catch (InboxConfigurationException ex)
            {
                // The batch was settled — every row released — before the processor rethrew. Only
                // this store's loop stops.
                StoreEngineLogs.LoopStopped(logger, ex, Loop, store.Value);
                return;
            }
            catch (Exception ex) when (HostStop.Is(ex, stoppingToken))
            {
                break;
            }
            catch (Exception ex)
            {
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
