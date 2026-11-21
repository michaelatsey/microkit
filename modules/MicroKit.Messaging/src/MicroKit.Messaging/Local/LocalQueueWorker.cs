namespace MicroKit.Messaging.Local;

using MicroKit.Messaging.Bus;

/// <summary>
/// Delivers the local messages of a host with no store, one (message, consumer) entry at a time,
/// through the receive pipeline — in a scope of its own — and settles each entry by its verdict
/// (#188 D7).
/// </summary>
/// <remarks>
/// <para>
/// The settlement mirrors the in-memory transport's consume loop, per entry: success forgets it; a
/// permanent verdict, or a transient one at <see cref="LocalQueue.MaxDeliveryAttempts"/>, dead-letters
/// it; a transient one below it redelivers it; a dependency outage redelivers it after a pause,
/// uncounted; a shutdown requeues it and ends the loop. A consumer's failure redelivers that
/// consumer only: one that succeeded never runs again. No exception from one entry ends the loop:
/// each entry is settled first and logged afterwards, through <see cref="GuardedLoop.Log"/>, so a failing
/// logging provider cannot end it either — the in-memory transport worker's rule (#186).
/// </para>
/// <para>
/// Takes only singletons that do not compose the registry, and resolves the pipeline when the loop
/// starts, so an invalid composition is reported by the start validation first.
/// </para>
/// </remarks>
internal sealed class LocalQueueWorker(
    LocalQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LocalQueueWorker> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MessageReceivePipeline pipeline;
        using (var scope = scopeFactory.CreateScope())
        {
            pipeline = scope.ServiceProvider.GetRequiredService<MessageReceivePipeline>();
        }

        // Under the module's one loop guard: only a host stop ends the loop, and no log call can.
        await GuardedLoop.RunAsync(
            async ct =>
            {
                var entry = await queue.DequeueAsync(ct).ConfigureAwait(false);
                return await ProcessOneAsync(pipeline, entry, ct).ConfigureAwait(false);
            },
            OnLoopFault,
            timeProvider,
            stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The delay after a fault outside any one message — the queue itself — so that a queue that
    /// keeps failing is retried, never spun on.
    /// </summary>
    private static readonly TimeSpan QueueFaultDelay = TimeSpan.FromSeconds(1);

    /// <summary>A fault of the loop itself, outside any message's verdict: reported, then retried.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The delay before the next pass.</returns>
    internal TimeSpan OnLoopFault(Exception fault)
    {
        GuardedLoop.Log(() => LocalLogs.LoopFaulted(logger, fault, QueueFaultDelay));
        return QueueFaultDelay;
    }

    /// <returns>
    /// The delay before the next message; <see langword="null"/> when the loop must stop, which only a
    /// host stop causes.
    /// </returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "No fault of one message may end the loop; it is settled as permanent.")]
    private async ValueTask<TimeSpan?> ProcessOneAsync(
        MessageReceivePipeline pipeline, LocalEntry entry, CancellationToken stoppingToken)
    {
        DeliveryOutcome outcome;
        try
        {
            outcome = await pipeline.DeliverLocalAsync(entry.Message, entry.ConsumerType, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (MessageReceivePipeline.IsShutdown(ex, stoppingToken))
        {
            outcome = new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            outcome = DeliveryOutcome.Permanent("An unexpected fault escaped the receive pipeline.", ex);
        }

        // Unsettled is a stop only while the host stops (HostStop); otherwise — a provider disposed
        // under the handler while the host runs — it is a failed attempt like any other.
        if (outcome.Verdict is DeliveryVerdict.Unsettled && !stoppingToken.IsCancellationRequested)
        {
            outcome = new DeliveryOutcome(DeliveryVerdict.Transient, outcome.Error, outcome.Reason ?? outcome.Error?.Message);
        }

        switch (outcome.Verdict)
        {
            case DeliveryVerdict.Succeeded:
                queue.Delivered(entry);
                return TimeSpan.Zero;

            case DeliveryVerdict.Permanent:
                var attempts = queue.FailedAttempts(entry);
                queue.DeadLetter(entry);
                GuardedLoop.Log(() => LocalLogs.DeadLettered(logger, outcome.Error, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, attempts, outcome.Reason));
                return TimeSpan.Zero;

            case DeliveryVerdict.Transient:
                var failed = queue.RecordFailedAttempt(entry);
                if (failed >= queue.MaxDeliveryAttempts)
                {
                    queue.DeadLetter(entry);
                    GuardedLoop.Log(() => LocalLogs.DeadLettered(logger, outcome.Error, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, failed, outcome.Reason));
                }
                else
                {
                    queue.Requeue(entry);
                    GuardedLoop.Log(() => LocalLogs.Redelivered(logger, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, failed, queue.MaxDeliveryAttempts));
                }

                return TimeSpan.Zero;

            case DeliveryVerdict.DependencyUnavailable:
                queue.Requeue(entry);
                GuardedLoop.Log(() => LocalLogs.Paused(logger, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, queue.DependencyUnavailableBackoff));
                return queue.DependencyUnavailableBackoff;

            default:
                // Unsettled while the host stops: requeued, count unchanged, and the loop ends — the
                // queue is in memory, so the message goes with the process.
                queue.Requeue(entry);
                return null;
        }
    }
}
