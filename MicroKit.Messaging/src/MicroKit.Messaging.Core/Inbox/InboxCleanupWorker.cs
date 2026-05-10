using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Inbox;

/// <summary>
/// Réprésente un service d'arrière-plan (Background Service) responsable du nettoyage périodique des messages obsolètes dans la boîte de réception (Inbox) du système de messagerie, en supprimant les messages qui ont été consommés et qui dépassent la période de rétention définie dans les options de configuration, afin de maintenir la performance et l'efficacité du système de messagerie en évitant l'accumulation de messages inutiles dans la boîte de réception.
/// </summary>
/// <seealso cref="Microsoft.Extensions.Hosting.BackgroundService" />
public class InboxCleanupWorker : BackgroundService
{
    /// <summary>
    /// The service provider
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;
    /// <summary>
    /// The options
    /// </summary>
    private readonly IOptions<InboxOptions> _options;
    /// <summary>
    /// The logger
    /// </summary>
    private readonly ILogger<InboxCleanupWorker> _logger;

    private const string CusumerGroup = "notifcations";

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxCleanupWorker"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service provider.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public InboxCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<InboxOptions> options,
        ILogger<InboxCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// This method is called when the <see cref="T:Microsoft.Extensions.Hosting.IHostedService" /> starts. The implementation should return a task that represents
    /// the lifetime of the long running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="M:Microsoft.Extensions.Hosting.IHostedService.StopAsync(System.Threading.CancellationToken)" /> is called.</param>
    /// <remarks>
    /// See <see href="https://learn.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.CleanupRunInterval.HasValue)
        {
            _logger.LogInformation("Inbox cleanup is disabled");
            return;
        }

        _logger.LogInformation("Inbox cleanup worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // 1. Récupérer les tenants et les consommateurs
                var tenantRegistry = scope.ServiceProvider.GetRequiredService<ITenantRegistry>();
                var consumerRegistry = scope.ServiceProvider.GetRequiredService<IInboxConsumerRegistry>();

                var tenants = await tenantRegistry.GetAllTenantsAsync(stoppingToken);
                var autoConsumers = consumerRegistry.GetConsumerNames();
                var consumers = autoConsumers.Union(_options.Value.CustomConsumers).ToList();
                foreach (var tenantId in tenants)
                {
                    // 2. Fixer le contexte pour le tenant actuel
                    // PRO : On crée un scope dédié par TENANT
                    using var tenantScope = _scopeFactory.CreateScope();

                    // On résout les services DEPUIS le nouveau scope
                    var store = tenantScope.ServiceProvider.GetRequiredService<ITenantStore>();
                    var tenant = await store.GetTenantAsync(tenantId, stoppingToken);
                    if (tenant == null) continue;

                    var setter = tenantScope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
                    setter.SetTenant(tenant);

                    // 3. Nettoyer pour chaque consommateur de ce tenant
                    foreach (var consumer in consumers)
                    {
                        await DoCleanupForTenantAndConsumerAsync(tenantId, consumer, stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error cleaning up inbox messages");
            }

            await Task.Delay(_options.Value.CleanupRunInterval.Value, stoppingToken);
        }
    }

    private async Task DoCleanupForTenantAndConsumerAsync(
        string tenantId, 
        string consumerName, 
        CancellationToken cancellation= default)
    {
        using var scope = _scopeFactory.CreateScope();
        var cleanupService = scope.ServiceProvider.GetRequiredService<IInboxCleanupService>();

        _logger.LogDebug("Starting inbox cleanup cycle.");

        // --- 1. NETTOYAGE DES MESSAGES CONSOMMES ---
        // On supprime ce qui est 'Consumed' et plus vieux que la RetentionPeriod
        var consumedCutoff = DateTimeOffset.UtcNow.Subtract(_options.Value.RetentionPeriod);

        // Nettoyage des consommés
        int deletedConsumed = await ExecuteDeletionLoop(
            cleanupService,
            tenantId, 
            consumerName,
            consumedCutoff,
            MessageStatus.Consumed,
            cancellation);
        if (deletedConsumed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Cleaned up {Count} consumed old inbox messages",
                deletedConsumed);
        }

        // --- 2. NETTOYAGE DES ÉCHECS ---
        // On garde les échecs plus longtemps pour analyse (ex: 30 jours)
        var failedCutoff = DateTimeOffset.UtcNow.Subtract(_options.Value.FailedRetentionPeriod);
        int deletedFailed = await ExecuteDeletionLoop(
            cleanupService,
            tenantId, 
            consumerName,
            failedCutoff,
            MessageStatus.Failed,
            cancellation);

        if (deletedFailed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Cleaned up {Count} failed outbox messages.", deletedFailed);
        }
    }

    private async Task<int> ExecuteDeletionLoop(
        IInboxCleanupService service,
        string tenantId, 
        string consumerName,
        DateTimeOffset cutoff,
        MessageStatus status,
        CancellationToken ct)
    {
        int totalDeleted = 0;
        int deletedInBatch;

        do
        {
            deletedInBatch = await service.CleanupAsync(
                tenantId,
                consumerName,
                cutoff,
                status,
                _options.Value.CleanupBatchSize,
                ct);

            totalDeleted += deletedInBatch;

            // Si on a atteint la taille du batch, c'est qu'il en reste probablement d'autres
        } while (deletedInBatch >= _options.Value.CleanupBatchSize && !ct.IsCancellationRequested);

        return totalDeleted;
    }
}
