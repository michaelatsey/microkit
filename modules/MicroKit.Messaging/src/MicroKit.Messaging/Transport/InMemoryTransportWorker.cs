namespace MicroKit.Messaging.Transport;

using MicroKit.Messaging.Bus;

/// <summary>
/// The consume loop of the in-memory transport: takes one envelope at a time off this service's
/// endpoint on the <see cref="InMemoryBroker"/>, runs it through the receive pipeline inline, and
/// settles it by the pipeline's verdict.
/// </summary>
/// <remarks>
/// <para>
/// <b>The endpoint is the service name</b> (#187), bound by <see cref="InMemoryEndpointBinder"/> on
/// the transport's first use or in <see cref="StartingAsync"/>, whichever comes first. The binder
/// validates the composition before it binds — the same checks as
/// <c>MessagingCompositionValidator</c> — so the order in which the host starts its services does
/// not matter. Retry counts and dead letters are
/// recorded on that endpoint.
/// </para>
/// <para>
/// <b>Only <see cref="IServiceScopeFactory"/> and singletons that do not compose the
/// registry</b> are taken by the constructor. The host resolves every hosted service before it
/// starts any, so a constructor dependency on the receive pipeline would build the registry — and
/// throw on an invalid composition — before the validator could list every problem. The pipeline
/// is resolved when the consume loop starts instead. Every message scope is
/// created per handler by the pipeline.
/// </para>
/// <para>
/// <b>Shutdown.</b> The host's stopping token is the pipeline's token. A message interrupted by it
/// comes back <see cref="DeliveryVerdict.Unsettled"/> and is requeued with its attempt count
/// unchanged, then the loop exits; the next host sharing the broker delivers it.
/// </para>
/// </remarks>
internal sealed class InMemoryTransportWorker(
    InMemoryBroker broker,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InMemoryTransportWorker> logger)
    : BackgroundService, IHostedLifecycleService
{
    /// <summary>
    /// Binds this service's endpoint, before any hosted service starts — unless the transport's
    /// first use already did.
    /// </summary>
    /// <param name="cancellationToken">The host's start token.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="IntegrationEventConfigurationException">The composition is invalid.</exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        EnsureBound();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A host that drives StartAsync without the lifecycle callbacks still gets its binding.
        var endpoint = EnsureBound();

        // A singleton, resolved through a scope only because a hosted service takes the scope
        // factory and nothing narrower; the scope owns none of it.
        MessageReceivePipeline pipeline;
        using (var scope = scopeFactory.CreateScope())
        {
            pipeline = scope.ServiceProvider.GetRequiredService<MessageReceivePipeline>();
        }

        // Under the module's one loop guard: only a host stop ends the loop, and no log call can.
        await GuardedLoop.RunAsync(
            async ct =>
            {
                var envelope = await broker.DequeueAsync(endpoint, ct).ConfigureAwait(false);
                return await ProcessOneAsync(pipeline, endpoint, envelope, ct).ConfigureAwait(false);
            },
            OnLoopFault,
            timeProvider,
            stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The delay after a fault outside any one envelope — the broker itself — so that a broker that
    /// keeps failing is retried, never spun on.
    /// </summary>
    private static readonly TimeSpan BrokerFaultDelay = TimeSpan.FromSeconds(1);

    /// <summary>A fault of the loop itself, outside any envelope's verdict: reported, then retried.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The delay before the next pass.</returns>
    internal TimeSpan OnLoopFault(Exception fault)
    {
        GuardedLoop.Log(() => BusLogs.InMemoryLoopFaulted(logger, fault, BrokerFaultDelay));
        return BrokerFaultDelay;
    }

    /// <summary>
    /// An unsettled delivery while the host runs (#188, review of 556bbc0). The root container
    /// cannot create a scope — disposed, or failing any other way: no service can be built until it
    /// is back — the dependency-outage path, requeued
    /// uncounted and paused, never a busy loop that dead-letters every message in milliseconds.
    /// Otherwise — a handler using a provider it captured from a disposed scope — a counted failed
    /// attempt, which dead-letters at the limit rather than pausing everything for ever.
    /// </summary>
    /// <param name="outcome">The unsettled outcome.</param>
    /// <returns>The outcome to settle.</returns>
    internal DeliveryOutcome WhileTheHostRuns(DeliveryOutcome outcome)
    {
        var reason = outcome.Reason ?? outcome.Error?.Message;
        return RootContainerDisposed()
            ? new DeliveryOutcome(DeliveryVerdict.DependencyUnavailable, outcome.Error, reason)
            : new DeliveryOutcome(DeliveryVerdict.Transient, outcome.Error, reason);
    }

    /// <summary>
    /// Whether no scope can be built. Any failure counts — another container reports its disposal
    /// with its own exception type — and none escapes: the probe runs after the message left the
    /// queue and before it is settled, so an escaping exception would lose it.
    /// </summary>
    private bool RootContainerDisposed()
    {
        try
        {
            using var probe = scopeFactory.CreateScope();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Binds the endpoint through the singleton <see cref="InMemoryEndpointBinder"/>, resolved through
    /// the scope factory — the only constructor dependency a background worker takes for services
    /// it resolves; the scope owns none of them.
    /// </summary>
    private string EnsureBound()
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<InMemoryEndpointBinder>().EnsureBound();
    }

    /// <summary>Delivers and settles one envelope. Never throws.</summary>
    /// <returns>
    /// The delay before the next envelope; <see langword="null"/> when the loop must stop, which only
    /// a host stop causes.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The safety net (#186): no exception from one message may end the consume loop, and the
    /// envelope — already off the queue — is never dropped. The pipeline turns every failure into a
    /// verdict; a fault that still escapes it is settled as permanent and dead-lettered.
    /// </para>
    /// <para>
    /// <b>Settle first, log afterwards.</b> Every log call of this loop goes through
    /// <see cref="GuardedLoop.Log"/>, so a failing logging provider cannot interrupt a settlement or escape
    /// the loop. <b>Settlement is idempotent per delivery</b>: once the envelope is requeued or
    /// dead-lettered, nothing settles it again, whatever fails afterwards.
    /// </para>
    /// </remarks>
    private async ValueTask<TimeSpan?> ProcessOneAsync(
        MessageReceivePipeline pipeline,
        string endpoint,
        MessageEnvelope envelope,
        CancellationToken stoppingToken)
    {
        var delivery = new Delivery(envelope, broker, endpoint);
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

        if (outcome.Verdict is DeliveryVerdict.Unsettled && !stoppingToken.IsCancellationRequested)
        {
            outcome = WhileTheHostRuns(outcome);
        }

        TimeSpan? next;
        try
        {
            next = Settle(delivery, outcome, logDisposition: escaped is null);
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

            next = stoppingToken.IsCancellationRequested ? null : TimeSpan.Zero;
        }

        if (escaped is not null)
        {
            // Logged once, after the settlement, naming what actually happened to the message.
            var disposition = delivery.Disposition;
            GuardedLoop.Log(() => BusLogs.UnexpectedDeliveryFault(
                logger, escaped, envelope.Id, envelope.Type, disposition));
        }

        return next;
    }

    /// <returns>The delay before the next envelope; <see langword="null"/> when the loop must stop.</returns>
    private TimeSpan? Settle(
        Delivery delivery,
        DeliveryOutcome outcome,
        bool logDisposition)
    {
        var envelope = delivery.Envelope;

        switch (outcome.Verdict)
        {
            case DeliveryVerdict.Succeeded:
                return TimeSpan.Zero;

            case DeliveryVerdict.Permanent:
                delivery.DeadLetter();
                if (logDisposition)
                {
                    var attempts = envelope.Id is null ? 0 : broker.GetFailedAttempts(delivery.Endpoint, envelope.Id);
                    GuardedLoop.Log(() => BusLogs.DeadLettered(
                        logger, outcome.Error, envelope.Id, envelope.Type, attempts, outcome.Reason));
                }

                return TimeSpan.Zero;

            case DeliveryVerdict.Transient:
                var failed = broker.RecordFailedAttempt(delivery.Endpoint, envelope);
                if (failed >= broker.MaxDeliveryAttempts)
                {
                    delivery.DeadLetter();
                    GuardedLoop.Log(() => BusLogs.DeadLettered(
                        logger, outcome.Error, envelope.Id, envelope.Type, failed, outcome.Reason));
                }
                else
                {
                    delivery.Requeue();
                    GuardedLoop.Log(() => BusLogs.Redelivered(
                        logger, envelope.Id, envelope.Type, failed, broker.MaxDeliveryAttempts));
                }

                return TimeSpan.Zero;

            case DeliveryVerdict.DependencyUnavailable:
                delivery.Requeue();
                GuardedLoop.Log(() => BusLogs.EndpointPaused(
                    logger, envelope.Id, envelope.Type, broker.DependencyUnavailableBackoff));
                return broker.DependencyUnavailableBackoff;

            case DeliveryVerdict.Unsettled:
                // Only a host stop reaches this arm (above): the message is requeued with its count
                // unchanged and the loop ends.
                delivery.Requeue();
                return null;

            default:
                throw new InvalidOperationException(
                    $"Unhandled {nameof(DeliveryVerdict)} '{outcome.Verdict}'.");
        }
    }

    /// <summary>
    /// One envelope's settlement, applied at most once: the first requeue or dead-letter wins and
    /// every later one is a no-op.
    /// </summary>
    private sealed class Delivery(MessageEnvelope envelope, InMemoryBroker broker, string endpoint)
    {
        private string? _disposition;

        public MessageEnvelope Envelope { get; } = envelope;

        public string Endpoint { get; } = endpoint;

        /// <summary>Gets what happened to the envelope, for the unexpected-fault log.</summary>
        public string Disposition => _disposition ?? "left unsettled";

        public void Requeue()
        {
            if (_disposition is not null)
            {
                return;
            }

            _disposition = "requeued for redelivery";
            broker.Requeue(Endpoint, Envelope);
        }

        public void DeadLetter()
        {
            if (_disposition is not null)
            {
                return;
            }

            _disposition = "dead-lettered";
            broker.DeadLetter(Endpoint, Envelope);
        }
    }
}
