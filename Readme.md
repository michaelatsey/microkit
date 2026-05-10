2. Comment l'utiliser (Exemple m?tier)
Imaginons un microservice de gestion de stock qui doit r?agir ? un ?v?nement "Commande Cr??e" provenant du service Commande.

Le Message (DTO)
C#
public record OrderCreatedIntegrationEvent(Guid OrderId, List<string> ItemIds);
Le Handler
Le d?veloppeur impl?mente simplement l'interface. Gr?ce ? ton ReflectionInboxConsumerRegistry, cette classe sera d?tect?e automatiquement au d?marrage.

C#
public class OrderCreatedHandler : IInboxHandler<OrderCreatedIntegrationEvent>
{
    private readonly IStockService _stockService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(
        IStockService stockService, 
        ITenantContext tenantContext,
        ILogger<OrderCreatedHandler> logger)
    {
        _stockService = stockService;
        _tenantContext = tenantContext; // Inject? automatiquement gr?ce au worker
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedIntegrationEvent message, CancellationToken ct = default)
    {
        // On peut v?rifier pour quel tenant on travaille
        var tenantId = _tenantContext.Tenant?.Id;
        _logger.LogInformation("R?servation du stock pour le client {TenantId}", tenantId);

        // Logique m?tier
        await _stockService.ReserveItemsAsync(message.ItemIds, ct);
    }
}
3. Comment ?a fonctionne sous le capot ?
Quand le InboxPublisherWorker tourne :

Il identifie le message dans la table InboxMessages.

Il voit que le ConsumerName correspond ? ordercreatedintegrationevent.

Le IInboxProcessor r?sout IInboxHandler<OrderCreatedIntegrationEvent> depuis le conteneur de services (DI).

Il d?s?rialise le JSON de la colonne Payload vers le type OrderCreatedIntegrationEvent.

Il appelle HandleAsync.

4. Enregistrement des Handlers dans la DI
Pour que le IInboxProcessor puisse r?soudre les handlers, il faut les enregistrer dans le conteneur de services. Tu peux ajouter cette logique de scan dans ton extension UseInbox :

C#
// Dans MicroKitInboxExtensions.cs
// On enregistre automatiquement toutes les impl?mentations de IInboxHandler dans la DI
var handlerTypes = assemblies.SelectMany(a => a.GetTypes())
    .Where(t => t is { IsClass: true, IsAbstract: false } &&
                t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInboxHandler<>)));

foreach (var handlerType in handlerTypes)
{
    var interfaceType = handlerType.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInboxHandler<>));
    services.AddScoped(interfaceType, handlerType);
}
Pourquoi c'est puissant ?
Injection de d?pendances : Le d?veloppeur peut injecter ce qu'il veut dans le constructeur (Repositories, Services, Contextes).

Multi-tenant transparent : Comme le worker a d?j? "set" le ITenantContext, le d?veloppeur n'a pas ? se soucier de filtrer ses donn?es par tenant manuellement si ses repositories utilisent le contexte.