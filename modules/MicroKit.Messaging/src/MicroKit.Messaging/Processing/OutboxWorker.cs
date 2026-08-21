namespace MicroKit.Messaging.Processing;

/// <summary>
/// Hosted background service driving the outbox loop. Resolves
/// <see cref="IOutboxCoordinator"/> from a fresh <see cref="AsyncServiceScope"/> on each
/// iteration so scoped services (notably <c>DbContext</c>) are disposed between passes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adaptive cadence.</b> The interval is derived from the result of the pass rather
/// than fixed. A saturated batch polls again immediately instead of sleeping while work
/// piles up; an idle queue backs off geometrically instead of spending a round trip
/// every few seconds forever; an unreachable transport backs off hard instead of
/// hammering a broker that is already down. On a connection-constrained database this
/// is a larger and more permanent saving than the round trips inside a batch.
/// </para>
/// <para>
/// <b>Resilience.</b> An <see cref="InvalidOperationException"/> resolving the
/// coordinator signals a misconfiguration: log critical and stop, since retrying a
/// missing registration cannot succeed. All other exceptions are transient: log and
/// keep polling.
/// </para>
/// <para>
/// <b>DI rule.</b> Only <see cref="IServiceScopeFactory"/> is injected. Scoped services
/// are always resolved from the per-iteration scope.
/// </para>
/// </remarks>
internal sealed class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    /// <summary>Initializes a new <see cref="OutboxWorker"/>.</summary>
    /// <param name="scopeFactory">Creates the per-iteration scope.</param>
    /// <param name="options">Polling cadence bounds and batch size.</param>
    /// <param name="logger">Logger.</param>
    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        OutboxProcessorOptions options,
        ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = _options.PollingInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                IOutboxCoordinator coordinator;
                try
                {
                    coordinator = scope.ServiceProvider.GetRequiredService<IOutboxCoordinator>();
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogCritical(
                        ex,
                        "Outbox worker misconfiguration — stopping worker. " +
                        "Register IOutboxProcessorStore (e.g. call AddEfCoreOutbox()).");
                    return;
                }

                var result = await coordinator.ExecuteAsync(stoppingToken).ConfigureAwait(false);
                delay = NextDelay(result, delay);
            }
            catch (OutboxConfigurationException ex)
            {
                // A required service is missing. The batch was already settled and released
                // by the processor, so nothing is stranded. Stop rather than back off:
                // retrying a missing registration cannot succeed without a redeployment,
                // and a worker that quietly retries forever hides the defect.
                _logger.LogCritical(
                    ex, "Outbox misconfiguration — stopping worker. Fix the registration and redeploy.");
                return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A throw from the coordinator means the claim itself failed — the database
                // is unreachable, not merely the broker. Back off on the same curve as a
                // transport outage rather than retrying at full polling speed.
                delay = Grow(delay, _options.MaxPollingInterval);

                _logger.LogError(
                    ex, "Transient outbox coordinator error. Retrying after {Interval}.", delay);
            }

            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Derives the next polling interval from the outcome of the pass.</summary>
    /// <param name="result">What the pass achieved.</param>
    /// <param name="current">The interval used before this pass.</param>
    /// <returns>The interval to wait before the next pass.</returns>
    internal TimeSpan NextDelay(OutboxBatchResult result, TimeSpan current) => result switch
    {
        // Broker down: every message would fail identically. Back off hard.
        { AbortReason: OutboxBatchAbortReason.TransportUnavailable }
            => Grow(current, _options.TransportUnavailableBackoff),

        // Shutting down: the delay is irrelevant, the loop is about to exit.
        { AbortReason: OutboxBatchAbortReason.Cancelled } => current,

        // Batch came back full: more work is almost certainly waiting. Do not sleep.
        var r when r.IsSaturated(_options.BatchSize) => TimeSpan.Zero,

        // Queue empty: stop paying a round trip every few seconds to learn nothing.
        { HasWork: false } => Grow(current, _options.MaxPollingInterval),

        // Partial batch: work exists but is keeping up. Return to the base cadence.
        _ => _options.PollingInterval,
    };

    private TimeSpan Grow(TimeSpan current, TimeSpan ceiling)
    {
        var next = current <= TimeSpan.Zero
            ? _options.PollingInterval
            : current * 2;

        return next > ceiling ? ceiling : next;
    }
}
