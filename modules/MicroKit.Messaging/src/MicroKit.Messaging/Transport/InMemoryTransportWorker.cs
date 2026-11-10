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

            var outcome = await pipeline.DeliverAsync(envelope, stoppingToken).ConfigureAwait(false);

            if (!await SettleAsync(envelope, outcome, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
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
                    broker.GetFailedAttempts(envelope.Id),
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
