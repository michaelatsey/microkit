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
    /// <para>
    /// The safety net (#186): no exception from one message may end the consume loop, and the
    /// envelope — already off the queue — is never dropped. The pipeline turns every failure into a
    /// verdict; a fault that still escapes it is settled as permanent and dead-lettered.
    /// </para>
    /// <para>
    /// <b>Settle first, log afterwards.</b> Every log call of this loop goes through
    /// <see cref="TryLog"/>, so a failing logging provider cannot interrupt a settlement or escape
    /// the loop. <b>Settlement is idempotent per delivery</b>: once the envelope is requeued or
    /// dead-lettered, nothing settles it again, whatever fails afterwards.
    /// </para>
    /// </remarks>
    private async ValueTask<bool> ProcessOneAsync(
        MessageEnvelope envelope, CancellationToken stoppingToken)
    {
        var delivery = new Delivery(envelope, broker);
        Exception? escaped = null;

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
            escaped = ex;
            outcome = DeliveryOutcome.Permanent("An unexpected fault escaped the receive pipeline.", ex);
        }

        bool keepConsuming;
        try
        {
            keepConsuming = await SettleAsync(delivery, outcome, logDisposition: escaped is null, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Settlement itself failed. If it had not yet settled the envelope, settle it now —
            // requeued when the host is stopping, dead-lettered otherwise; if it had, leave it.
            escaped ??= ex;
            if (stoppingToken.IsCancellationRequested)
            {
                delivery.Requeue();
            }
            else
            {
                delivery.DeadLetter();
            }

            keepConsuming = !stoppingToken.IsCancellationRequested;
        }

        if (escaped is not null)
        {
            // Logged once, after the settlement, naming what actually happened to the message.
            var disposition = delivery.Disposition;
            TryLog(() => BusLogs.UnexpectedDeliveryFault(
                logger, escaped, envelope.Id, envelope.Type, disposition));
        }

        return keepConsuming;
    }

    /// <returns><see langword="false"/> when the loop must stop.</returns>
    private async ValueTask<bool> SettleAsync(
        Delivery delivery,
        DeliveryOutcome outcome,
        bool logDisposition,
        CancellationToken stoppingToken)
    {
        var envelope = delivery.Envelope;

        switch (outcome.Verdict)
        {
            case DeliveryVerdict.Succeeded:
                return true;

            case DeliveryVerdict.Permanent:
                delivery.DeadLetter();
                if (logDisposition)
                {
                    var attempts = envelope.Id is null ? 0 : broker.GetFailedAttempts(envelope.Id);
                    TryLog(() => BusLogs.DeadLettered(
                        logger, outcome.Error, envelope.Id, envelope.Type, attempts, outcome.Reason));
                }

                return true;

            case DeliveryVerdict.Transient:
                var failed = broker.RecordFailedAttempt(envelope);
                if (failed >= broker.MaxDeliveryAttempts)
                {
                    delivery.DeadLetter();
                    TryLog(() => BusLogs.DeadLettered(
                        logger, outcome.Error, envelope.Id, envelope.Type, failed, outcome.Reason));
                }
                else
                {
                    delivery.Requeue();
                    TryLog(() => BusLogs.Redelivered(
                        logger, envelope.Id, envelope.Type, failed, broker.MaxDeliveryAttempts));
                }

                return true;

            case DeliveryVerdict.DependencyUnavailable:
                delivery.Requeue();
                TryLog(() => BusLogs.EndpointPaused(
                    logger, envelope.Id, envelope.Type, broker.DependencyUnavailableBackoff));
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
                delivery.Requeue();
                return false;

            default:
                throw new InvalidOperationException(
                    $"Unhandled {nameof(DeliveryVerdict)} '{outcome.Verdict}'.");
        }
    }

    /// <summary>
    /// Runs one log call. A logging provider that throws must never interrupt a settlement or end
    /// the consume loop, and there is nowhere left to report its failure, so it is swallowed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failing logger must never end the consume loop; nothing is left to report to.")]
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

    /// <summary>
    /// One envelope's settlement, applied at most once: the first requeue or dead-letter wins and
    /// every later one is a no-op.
    /// </summary>
    private sealed class Delivery(MessageEnvelope envelope, InMemoryBroker broker)
    {
        private string? _disposition;

        public MessageEnvelope Envelope { get; } = envelope;

        /// <summary>Gets what happened to the envelope, for the unexpected-fault log.</summary>
        public string Disposition => _disposition ?? "left unsettled";

        public void Requeue()
        {
            if (_disposition is not null)
            {
                return;
            }

            _disposition = "requeued for redelivery";
            broker.Requeue(Envelope);
        }

        public void DeadLetter()
        {
            if (_disposition is not null)
            {
                return;
            }

            _disposition = "dead-lettered";
            broker.DeadLetter(Envelope);
        }
    }
}
