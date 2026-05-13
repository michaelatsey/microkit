# MicroKit.Messaging

Solution robuste de messagerie transactionnelle pour .NET 8/9. Implémente les patterns **Outbox** et **Inbox** avec une gestion native du verrouillage SQL pour garantir la consistance des données dans des architectures distribuées.

## 🛠 Features

* **Transactional Outbox** : Publication d'événements atomique avec vos changements d'état métier.
* **Idempotent Inbox** : Détection et prévention des doublons de messages entrants pour garantir un traitement unique (Exactly-once processing).
* **Pessimistic Locking** : Support de la haute disponibilité (scaling horizontal) via `UPDLOCK/READPAST` (SQL Server) et `FOR UPDATE SKIP LOCKED` (PostgreSQL).
* **Configuration Hybride** : Support complet du `appsettings.json` et/ou de l'API Fluent.
* **Performance** : Intégration optimisée avec `IDbContextFactory` pour limiter l'empreinte mémoire des Background Workers.
* **Validation au Démarrage** : Validation stricte des options via DataAnnotations et `IValidateOptions` dès le lancement de l'application.
* **Nettoyage Automatique** : Background workers dédiés à la purge des messages traités pour éviter la saturation des tables.

## Configuration

### 1. Via appsettings.json

La bibliothèque bind automatiquement la section `MicroKit:Messaging:Outbox`.

```json
{
  "MicroKit": {
    "Messaging": {
      "Outbox": {
        "PollingInterval": "00:00:05",
        "BatchSize": 100,
        "CleanupRunInterval": "01:00:00",
        "RetentionPeriod": "7.00:00:00"
      },
      "Inbox": {
        "CleanupRunInterval": "01:00:00",
        "RetentionPeriod": "2.00:00:00"
      }
    }
  }
}

```

### 2. Initialisation (Fluent API)

L'enregistrement respecte la séparation des responsabilités. Le développeur fournit le `DbContext`, MicroKit fournit la logique.

```csharp
services.AddMicroKitMessaging(config =>
{
    config
        .UseOutbox(options => {
            // Ces réglages surchargent le appsettings.json si nécessaire
            options.BatchSize = 50;
        })
        .UseInbox(options => {
            options.Enabled = true; // Active le tracking des messages entrants
        })
        .UseSystemTextJson()
        .UseEfCorePersistence<ApplicationDbContext>()
        .UseMediatRPublisher();
});

```

---

## Implémentation

### Configuration du Modèle

Appliquez les configurations de tables dans votre `OnModelCreating` :

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
}

```

### Usage : Publication Outbox

Injectez `IOutboxService` pour persister vos messages durant la transaction :

```csharp
public async Task Handle(CreateOrderCommand command, CancellationToken ct)
{
    var order = new Order(command.Amount);
    _context.Orders.Add(order);

    // Enregistrement dans l'outbox (table technique)
    await _outboxService.SaveAsync(new OrderCreatedEvent(order.Id), ct);

    // Sauvegarde atomique (Métier + Outbox)
    await _context.SaveChangesAsync(ct);
}

```

---

## Mécanique Interne

### Stratégie de Verrouillage

La lib détecte dynamiquement le provider SQL pour injecter la stratégie de verrouillage la plus performante. Cela permet à plusieurs instances de l'application de consommer la même table sans collision :

* **SQL Server** : `WITH (UPDLOCK, READPAST, ROWLOCK)`
* **Postgres** : `FOR UPDATE SKIP LOCKED`

### Gestion du Cycle de Vie (DbContext)

Pour éviter les données périmées ou les fuites mémoire dans les workers de longue durée, la lib :

1. Utilise la `IDbContextFactory<T>` pour générer des contextes isolés.
2. Invoque systématiquement `ChangeTracker.Clear()` avant la désérialisation des messages lockés pour garantir l'intégrité des données mappées depuis le SQL brut (`OUTPUT INSERTED`).

---

## Validation & Sécurité

* **OutboxOptionsValidator** : Valide la cohérence des `TimeSpan` (ex: intervalle de polling positif).
* **MessagingModuleValidator** : Vérifie au démarrage que toutes les dépendances critiques (comme un `IOutboxPublisher`) sont correctement enregistrées avant de lancer les Background Workers.

---

# Interceptors

```csharp
namespace Nexus.Messaging.Persistence.EFCore.Interceptors;

public class OutboxTransactionInterceptor : SaveChangesInterceptor
{
    private readonly IOutboxService _outboxService;
    
    public OutboxTransactionInterceptor(IOutboxService outboxService)
    {
        _outboxService = outboxService;
    }
    
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return result;
        
        // Check if there are domain events to publish
        var entities = context.ChangeTracker.Entries()
            .Where(e => e.Entity is IHasDomainEvents)
            .Select(e => (IHasDomainEvents)e.Entity)
            .ToList();
        
        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                // Convert domain event to integration event and save to outbox
                await SaveDomainEventToOutbox(
                    domainEvent, 
                    context, 
                    cancellationToken);
            }
            
            entity.ClearDomainEvents();
        }
        
        return result;
    }
    
    private async Task SaveDomainEventToOutbox(
        IDomainEvent domainEvent,
        DbContext context,
        CancellationToken cancellationToken)
    {
        // This would be implemented based on your domain
        // Example: Convert OrderCreatedDomainEvent to OrderCreatedIntegrationEvent
        
        // For now, we'll save a generic message
        await _outboxService.EnqueueAsync(
            domainEvent.GetType().Name,
            JsonSerializer.Serialize(domainEvent),
            "events/" + domainEvent.GetType().Name.Replace("Event", "").ToLower(),
            correlationId: context.GetCorrelationId(),
            cancellationToken: cancellationToken);
    }
}
```