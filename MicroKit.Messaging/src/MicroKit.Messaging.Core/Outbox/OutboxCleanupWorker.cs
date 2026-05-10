using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Outbox;

public class OutboxCleanupWorker: BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxCleanupWorker> _logger;

    public OutboxCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CleanupRunInterval.HasValue)
        {
            _logger.LogInformation("Outbox cleanup is disabled.");
            return;
        }

        _logger.LogInformation("Outbox Cleanup Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenantRegistry = scope.ServiceProvider.GetRequiredService<ITenantRegistry>();
                // On récupère tous les tenants actifs
                var tenants = await tenantRegistry.GetAllTenantsAsync(stoppingToken);

                foreach (var tenantId in tenants)
                {
                    // Pour l'Outbox, on n'a pas besoin de fixer le ITenantContext car 
                    // le service de cleanup prend le tenantId en paramètre explicite.
                    await DoCleanupForTenantAsync(tenantId, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during outbox cleanup cycle.");
            }

            // Utilisation de l'intervalle dédié au nettoyage
            await Task.Delay(_options.CleanupRunInterval.Value, stoppingToken);
        }
    }

    private async Task DoCleanupForTenantAsync( string tenantId,  CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        // 1. RÉSOUDRE ET FIXER LE TENANT DANS LE CONTEXTE DU SCOPE
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenant = await store.GetTenantAsync(tenantId, cancellationToken);

        if (tenant == null)
        {
            _logger.LogWarning("Cleanup skipped: Tenant {TenantId} not found in store.", tenantId);
            return;
        }

        var setter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        setter.SetTenant(tenant);

        // 2. RÉCUPÉRER LE SERVICE (qui bénéficiera maintenant du contexte fixé)
        var cleanupService = scope.ServiceProvider.GetRequiredService<IOutboxCleanupService>();

        _logger.LogDebug("Starting outbox cleanup cycle.");

        // --- 1. NETTOYAGE DES MESSAGES PUBLIÉS ---
        // On garde les succès pendant une période courte (ex: 7 jours)
        var publishedCutoff = DateTimeOffset.UtcNow.Subtract(_options.RetentionPeriod);

        int deletedPublished = await ExecuteDeletionLoop(
            cleanupService,
            tenantId,
            publishedCutoff,
            MessageStatus.Published,
            cancellationToken);

        if (deletedPublished > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Cleaned up {Count} published outbox messages.", deletedPublished);
        }

        // --- 2. NETTOYAGE DES ÉCHECS ---
        // On garde les échecs plus longtemps pour analyse (ex: 30 jours)
        var failedCutoff = DateTimeOffset.UtcNow.Subtract(_options.FailedRetentionPeriod);
        int deletedFailed = await ExecuteDeletionLoop(
            cleanupService,
            tenantId,
            failedCutoff,
            MessageStatus.Failed,
            cancellationToken);

        if ((deletedPublished > 0 || deletedFailed > 0) && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Cleaned up outbox for tenant {TenantId}: {Published} published, {Failed} failed",
                tenantId, deletedPublished, deletedFailed);
        }
    }

    /// <summary>
    /// Exécute la suppression par batch jusqu'à ce qu'il n'y ait plus rien à supprimer pour le critère donné.
    /// </summary>
    private async Task<int> ExecuteDeletionLoop(
        IOutboxCleanupService service,
        string tenantId,
        DateTimeOffset cutoff,
        MessageStatus status,
        CancellationToken cancellationToken)
    {
        int totalDeleted = 0;
        int deletedInBatch;

        do
        {
            deletedInBatch = await service.CleanupAsync(
                cutoff,
                status,
                _options.CleanupBatchSize,
                tenantId, // Passage explicite du Tenant
                cancellationToken);

            totalDeleted += deletedInBatch;

            if (deletedInBatch >= _options.CleanupBatchSize)
                await Task.Delay(50, cancellationToken); // Petite pause pour la DB

            // Si on a atteint la taille du batch, c'est qu'il en reste probablement d'autres
        } while (deletedInBatch >= _options.CleanupBatchSize && !cancellationToken.IsCancellationRequested);

        return totalDeleted;
    }
}
