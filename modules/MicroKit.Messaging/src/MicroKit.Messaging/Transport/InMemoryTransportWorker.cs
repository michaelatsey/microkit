namespace MicroKit.Messaging.Transport;

using MicroKit.Messaging.Bus;

/// <summary>
/// The consume loop of the in-memory transport: takes one envelope at a time off the
/// <see cref="InMemoryBroker"/>, runs it through the receive pipeline inline, and settles it by
/// the pipeline's verdict.
/// </summary>
/// <remarks>
/// <para>
/// The only hosted service #186 registers. It depends on singletons only; every scope is created
/// per handler by the pipeline.
/// </para>
/// <para>
/// <b>Shutdown.</b> The host's stopping token is the pipeline's token. A message interrupted by it
/// comes back <see cref="DeliveryVerdict.Unsettled"/> and is requeued with its attempt count
/// unchanged, then the loop exits; the next host sharing the broker delivers it.
/// </para>
/// </remarks>
internal sealed class InMemoryTransportWorker(
    InMemoryBroker broker,
    MessageReceivePipeline pipeline,
    TimeProvider timeProvider,
    ILogger<InMemoryTransportWorker> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            MessageEnvelope envelope;
            try
            {
                envelope = await broker.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!await ProcessOneAsync(envelope, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>Delivers and settles one envelope. Never throws.</summary>
    /// <returns><see langword="false"/> when the loop must stop, which only shutdown causes.</returns>
    /// <remarks>
    /// The safety net (#186): no exception from one message may end the consume loop. The
    /// pipeline already turns every failure into a verdict; anything that still escapes it, or the
    /// settlement, is logged at <c>Error</c> and the message is dead-lettered — never dropped,
    /// since it is already off the queue.
    /// </remarks>
    private async ValueTask<bool> ProcessOneAsync(
        MessageEnvelope envelope, CancellationToken stoppingToken)
    {
        DeliveryOutcome outcome;
        try
        {
            outcome = await pipeline.DeliverAsync(envelope, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (MessageReceivePipeline.IsShutdown(ex, stoppingToken))
        {
            outcome = new DeliveryOutcome(DeliveryVerdict.Unsettled, ex);
        }
        catch (Exception ex)
        {
            BusLogs.UnexpectedDeliveryFault(logger, ex, envelope.Id, envelope.Type);
            outcome = DeliveryOutcome.Permanent("An unexpected fault escaped the receive pipeline.", ex);
        }

        try
        {
            return await SettleAsync(envelope, outcome, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (outcome.Verdict is not DeliveryVerdict.Permanent)
        {
            BusLogs.UnexpectedDeliveryFault(logger, ex, envelope.Id, envelope.Type);
            broker.DeadLetter(envelope);
            return !stoppingToken.IsCancellationRequested;
        }
        catch (Exception ex)
        {
            // The permanent settlement itself failed after the envelope was dead-lettered, or
            // before: DeadLetter is the first thing it does, so the message is not lost.
            BusLogs.UnexpectedDeliveryFault(logger, ex, envelope.Id, envelope.Type);
            return !stoppingToken.IsCancellationRequested;
        }
    }

    /// <returns><see langword="false"/> when the loop must stop.</returns>
    private async ValueTask<bool> SettleAsync(
        MessageEnvelope envelope, DeliveryOutcome outcome, CancellationToken stoppingToken)
    {
        switch (outcome.Verdict)
        {
            case DeliveryVerdict.Succeeded:
                return true;

            case DeliveryVerdict.Permanent:
                broker.DeadLetter(envelope);
                BusLogs.DeadLettered(
                    logger,
                    outcome.Error,
                    envelope.Id,
                    envelope.Type,
                    envelope.Id is null ? 0 : broker.GetFailedAttempts(envelope.Id),
                    outcome.Reason);
                return true;

            case DeliveryVerdict.Transient:
                var failed = broker.RecordFailedAttempt(envelope);
                if (failed >= broker.MaxDeliveryAttempts)
                {
                    broker.DeadLetter(envelope);
                    BusLogs.DeadLettered(
                        logger, outcome.Error, envelope.Id, envelope.Type, failed, outcome.Reason);
                }
                else
                {
                    BusLogs.Redelivered(
                        logger, envelope.Id, envelope.Type, failed, broker.MaxDeliveryAttempts);
                    broker.Requeue(envelope);
                }

                return true;

            case DeliveryVerdict.DependencyUnavailable:
                broker.Requeue(envelope);
                BusLogs.EndpointPaused(
                    logger, envelope.Id, envelope.Type, broker.DependencyUnavailableBackoff);
                try
                {
                    await Task.Delay(broker.DependencyUnavailableBackoff, timeProvider, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return false;
                }

                return true;

            case DeliveryVerdict.Unsettled:
                // Only genuine shutdown yields this verdict — a cancelled host token or a disposed
                // provider — so the message is requeued with its count unchanged and the loop ends.
                broker.Requeue(envelope);
                return false;

            default:
                throw new InvalidOperationException(
                    $"Unhandled {nameof(DeliveryVerdict)} '{outcome.Verdict}'.");
        }
    }
}
