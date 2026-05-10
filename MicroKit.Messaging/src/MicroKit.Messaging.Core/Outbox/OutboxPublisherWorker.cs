using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Outbox;

/// <summary>
/// Réprésente un service d'arrière-plan (Background Service) responsable de la publication des messages en attente dans la boîte d'envoi (Outbox) du système de messagerie, permettant de traiter les messages de manière asynchrone et fiable, en respectant les paramètres de configuration définis pour la boîte d'envoi, tels que la taille des lots, l'intervalle de sondage et les stratégies de réessai en cas d'échec de publication.
/// </summary>
/// <seealso cref="Microsoft.Extensions.Hosting.BackgroundService" />
public class OutboxPublisherWorker: BackgroundService
{
    /// <summary>
    /// The service provider
    /// </summary>
    private readonly IServiceScopeFactory _serviceScopeFactory;
    /// <summary>
    /// The options
    /// </summary>
    private readonly IOptions<OutboxOptions> _options;
    /// <summary>
    /// The logger
    /// </summary>
    private readonly ILogger<OutboxPublisherWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxPublisherWorker"/> class.
    /// </summary>
    /// <param name="serviceScopeFactory">The service provider.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public OutboxPublisherWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxPublisherWorker> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
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
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Outbox publisher is disabled");
            return;
        }

        _logger.LogInformation("Outbox publisher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Scope global juste pour découvrir les tenants
                await using var discoveryScope = _serviceScopeFactory.CreateAsyncScope();
                // On récupère les IDs des tenants qui ont des messages à traiter
                var tenantDiscovery = discoveryScope.ServiceProvider.GetRequiredService<ITenantRegistry>();
                var activeTenants = await tenantDiscovery.GetAllTenantsAsync(stoppingToken);

                foreach (var tenantId in activeTenants)
                {
                    // PRO: Un discoveryScope ISOLÉ par tenant
                    await using var tenantScope = _serviceScopeFactory.CreateAsyncScope();

                    // 1. Résoudre le store et le setter
                    var store = tenantScope.ServiceProvider.GetRequiredService<ITenantStore>();
                    var setter = tenantScope.ServiceProvider.GetRequiredService<ITenantContextSetter>();

                    // 2. Fixer le contexte
                    var tenant = await store.GetTenantAsync(tenantId, stoppingToken);
                    if (tenant == null) continue;
                    setter.SetTenant(tenant);

                    // 3. Maintenant on résout le processeur (il aura le bon DbContext/Contexte)
                    var processor = tenantScope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                    // On traite un batch pour CE tenant spécifique
                    await processor.ProcessBatchAsync(
                        tenantId,
                        _options.Value.BatchSize,
                        stoppingToken);
                }

            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(_options.Value.PollingInterval, stoppingToken);
        }
    }
}
