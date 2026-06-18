namespace MicroKit.Messaging.Processing;

/// <summary>
/// Hosted background service that drives the outbox processing loop.
/// Resolves <see cref="IOutboxCoordinator"/> from a fresh <see cref="AsyncServiceScope"/>
/// on each iteration so that scoped services (e.g. <c>DbContext</c>) are properly disposed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Resilience model:</strong>
/// An <see cref="InvalidOperationException"/> when resolving <see cref="IOutboxCoordinator"/>
/// signals a misconfiguration (e.g. <c>IOutboxProcessorStore</c> not registered). The worker
/// logs a critical message and stops — the host continues, operator must fix the registration.
/// All other exceptions from the coordinator are treated as transient: the worker logs an error
/// and continues polling after <see cref="OutboxProcessorOptions.PollingInterval"/>.
/// </para>
/// <para>
/// <strong>DI rule:</strong> only <see cref="IServiceScopeFactory"/> is injected in the
/// constructor. Scoped services are always resolved from the per-iteration scope.
/// </para>
/// </remarks>
internal sealed class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    /// <summary>
    /// Initializes a new <see cref="OutboxWorker"/>.
    /// </summary>
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

                await coordinator.ExecuteAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Transient outbox coordinator error. Retrying after {Interval}.",
                    _options.PollingInterval);
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
