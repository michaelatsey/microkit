using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Inbox;

/// <summary>
/// Réprésente un service d'arrière-plan (Background Service) responsable de la publication des messages en attente dans la boîte d'envoi (Outbox) du système de messagerie, permettant de traiter les messages de manière asynchrone et fiable, en respectant les paramètres de configuration définis pour la boîte d'envoi, tels que la taille des lots, l'intervalle de sondage et les stratégies de réessai en cas d'échec de publication.
/// </summary>
/// <seealso cref="Microsoft.Extensions.Hosting.BackgroundService" />
public class InboxPublisherWorker : BackgroundService
{
    /// <summary>
    /// The service provider
    /// </summary>
    private readonly IServiceScopeFactory _serviceScopeFactory;
    /// <summary>
    /// The options
    /// </summary>
    private readonly IOptions<InboxOptions> _options;
    /// <summary>
    /// The logger
    /// </summary>
    private readonly ILogger<InboxPublisherWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxPublisherWorker"/> class.
    /// </summary>
    /// <param name="serviceScopeFactory">The service provider.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public InboxPublisherWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<InboxOptions> options,
        ILogger<InboxPublisherWorker> logger)
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

        // On résout le registre des consommateurs une seule fois au démarrage
        // (Généralement Singleton ou résolu via un scope temporaire ici)
        using var initScope = _serviceScopeFactory.CreateScope();
        var consumerRegistry = initScope.ServiceProvider.GetRequiredService<IInboxConsumerRegistry>();
        var autoConsumers = consumerRegistry.GetConsumerNames();
        var consumers = autoConsumers.Union(_options.Value.CustomConsumers).ToList();

        // Le Worker "Haute Performance"
        // Pour optimiser le débit, on utilise un SemaphoreSlim.Cela permet de traiter, par exemple,
        // 5 tenants en parallèle tout en restant sur un seul thread pour la boucle principale.
        using var semaphore = new SemaphoreSlim(_options.Value.MaxDegreeOfParallelism);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<IInboxProcessor>();

                // 1. Récupération de la liste des tenantIdentifiers (Registry)
                var tenantRegistry = scope.ServiceProvider.GetRequiredService<ITenantRegistry>();
                var tenantIdentifiers = await tenantRegistry.GetAllTenantsAsync(stoppingToken);

                var tasks = tenantIdentifiers.Select(async tenantIdentifier =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        await ProcessTenantAsync(tenantIdentifier, consumers, stoppingToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(_options.Value.PollingInterval, stoppingToken);
        }
    }


    private async Task ProcessTenantAsync(string tenantId, IReadOnlyList<string> consumers, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceScopeFactory.CreateAsyncScope();

        // 1. Résolution de l'objet ITenant complet via le Store
        var store = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenant = await store.GetTenantAsync(tenantId, cancellationToken);

        if (tenant == null) return;

        // 2. Injection dans le contexte via ton Setter
        var setter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        setter.SetTenant(tenant);

        // 3. Boucle sur les consommateurs pour ce tenant
        var processor = scope.ServiceProvider.GetRequiredService<IInboxProcessor>();
        foreach (var consumer in consumers)
        {
            await processor.ProcessBatchAsync(tenantId, consumer, _options.Value.BatchSize, cancellationToken);
        }
    }
}
