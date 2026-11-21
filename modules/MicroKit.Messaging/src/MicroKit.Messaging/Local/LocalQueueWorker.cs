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
/// each entry is settled first and logged afterwards, through <see cref="TryLog"/>, so a failing
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

        while (!stoppingToken.IsCancellationRequested)
        {
            LocalEntry entry;
            try
            {
                entry = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!await ProcessOneAsync(pipeline, entry, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <returns><see langword="false"/> when the loop must stop, which only shutdown causes.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "No fault of one message may end the loop; it is settled as permanent.")]
    private async ValueTask<bool> ProcessOneAsync(
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

        switch (outcome.Verdict)
        {
            case DeliveryVerdict.Succeeded:
                queue.Delivered(entry);
                return true;

            case DeliveryVerdict.Permanent:
                var attempts = queue.FailedAttempts(entry);
                queue.DeadLetter(entry);
                TryLog(() => LocalLogs.DeadLettered(logger, outcome.Error, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, attempts, outcome.Reason));
                return true;

            case DeliveryVerdict.Transient:
                var failed = queue.RecordFailedAttempt(entry);
                if (failed >= queue.MaxDeliveryAttempts)
                {
                    queue.DeadLetter(entry);
                    TryLog(() => LocalLogs.DeadLettered(logger, outcome.Error, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, failed, outcome.Reason));
                }
                else
                {
                    queue.Requeue(entry);
                    TryLog(() => LocalLogs.Redelivered(logger, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, failed, queue.MaxDeliveryAttempts));
                }

                return true;

            case DeliveryVerdict.DependencyUnavailable:
                queue.Requeue(entry);
                TryLog(() => LocalLogs.Paused(logger, entry.Message.Id.Value, entry.Message.EventType, entry.ConsumerType, queue.DependencyUnavailableBackoff));
                try
                {
                    await Task.Delay(queue.DependencyUnavailableBackoff, timeProvider, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return false;
                }

                return true;

            default:
                // Unsettled: only shutdown yields it. Requeued, count unchanged, and the loop ends —
                // the queue is in memory, so the message goes with the process.
                queue.Requeue(entry);
                return false;
        }
    }

    /// <summary>Runs a log call; a logging provider's exception is swallowed, never the loop's end.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A logging provider's fault must not end the loop or undo a settlement.")]
    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch (Exception)
        {
            // Deliberately swallowed: see the summary.
        }
    }
}
