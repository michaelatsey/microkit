namespace MicroKit.Messaging.Processing;

/// <summary>
/// Hosted background service that drives the inbox processing loop.
/// Resolves <see cref="IInboxCoordinator"/> from a fresh <see cref="AsyncServiceScope"/>
/// on each iteration so that scoped services (e.g. <c>DbContext</c>) are properly disposed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Resilience model:</strong>
/// An <see cref="InvalidOperationException"/> when resolving <see cref="IInboxCoordinator"/>
/// signals a misconfiguration (e.g. <c>IInboxStore</c> not registered). The worker logs a
/// critical message and stops — the host continues, operator must fix the registration.
/// All other exceptions from the coordinator are treated as transient: the worker logs an
/// error and continues polling after <see cref="InboxProcessorOptions.PollingInterval"/>.
/// </para>
/// <para>
/// <strong>DI rule:</strong> only <see cref="IServiceScopeFactory"/> is injected in the
/// constructor. Scoped services are always resolved from the per-iteration scope.
/// </para>
/// </remarks>
internal sealed class InboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InboxProcessorOptions _options;
    private readonly ILogger<InboxWorker> _logger;

    /// <summary>
    /// Initializes a new <see cref="InboxWorker"/>.
    /// </summary>
    public InboxWorker(
        IServiceScopeFactory scopeFactory,
        InboxProcessorOptions options,
        ILogger<InboxWorker> logger)
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

                IInboxCoordinator coordinator;
                try
                {
                    coordinator = scope.ServiceProvider.GetRequiredService<IInboxCoordinator>();
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogCritical(
                        ex,
                        "Inbox worker misconfiguration — stopping worker. " +
                        "Register IInboxStore (e.g. call AddEfCoreOutbox()).");
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
                    "Transient inbox coordinator error. Retrying after {Interval}.",
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
