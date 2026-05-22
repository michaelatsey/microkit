# Skill: CQRS Domain Modeling

## Quand activer ce skill
- Conception d'une nouvelle feature (command + query à créer)
- Décision de découpage d'un use case en Command/Query
- Modélisation des DomainEvents associés à un agrégat
- Review d'un handler qui semble trop faire

## Comment découper un use case en CQRS

### Étape 1 — identifier l'intention
```
"L'utilisateur veut..."
  → changer quelque chose     = Command
  → obtenir quelque chose     = Query
  → être notifié de quelque chose = Event (handler en écoute)
```

### Étape 2 — identifier les données en jeu
```
Command : données en entrée (ce que l'utilisateur envoie)
  → sealed record avec les champs strictement nécessaires
  → pas de données de lecture en retour (sauf l'ID créé)

Query : critères de recherche en entrée + projection en sortie
  → sealed record avec les filtres
  → DTO en sortie (jamais l'entité de domaine directement)
```

### Étape 3 — identifier les events déclenchés
```
Après chaque Command réussie → quels faits sont nés ?
  CreateOrderCommand réussie → OrderCreatedEvent
  ShipOrderCommand réussie   → OrderShippedEvent, InventoryDeductedEvent
  RegisterUserCommand réussie → UserRegisteredEvent
```

## Exemples de découpage

### ❌ Mauvais découpage (Command qui retourne trop)
```csharp
// Retourne l'entité complète → la Command fait aussi le travail de la Query
public sealed record CreateUserCommand(...) : ICommand<Result<UserDto>>;
```
### ✅ Bon découpage
```csharp
// Command retourne juste l'ID
public sealed record CreateUserCommand(...) : ICommand<Result<UserId>>;

// Query séparée pour lire l'entité
public sealed record GetUserByIdQuery(UserId UserId) : IQuery<Result<UserDto>>;

// Le appelant envoie les deux si besoin :
var userId = await _mediator.SendCommandAsync(new CreateUserCommand(...));
var user   = await _mediator.SendQueryAsync(new GetUserByIdQuery(userId));
```

### ❌ Mauvais découpage (Query qui mute)
```csharp
// "GetOrCreate" = Command + Query mélangés
public sealed record GetOrCreateCartQuery(Guid UserId) : IQuery<Result<CartDto>>;
// → le handler crée un panier si absent = effet de bord = violation CQS
```
### ✅ Bon découpage
```csharp
// Deux intentions séparées
public sealed record EnsureCartExistsCommand(Guid UserId) : ICommand<Result<CartId>>;
public sealed record GetCartQuery(CartId CartId) : IQuery<Result<CartDto>>;
```

## Quand utiliser Result<T> vs T direct

| Situation | Type retour recommandé |
|---|---|
| Opération qui peut ne pas trouver | `Result<T>` avec `NotFoundError` |
| Opération avec règles métier pouvant échouer | `Result<T>` avec erreur typée |
| Command qui crée une ressource | `Result<EntityId>` |
| Query sur donnée de config (toujours présente) | `T` direct |
| Stream de données | `IAsyncEnumerable<T>` (pas de Result) |
| Void-like command (fire and forget) | `Result<Unit>` ou `ICommand` non-generic |

## DomainEvent : granularité

```
Trop fin (un event par champ modifié) → bruit, handlers inutiles
Trop gros (un event "UserUpdated" fourre-tout) → perd la sémantique

✅ Un event = un fait métier identifiable
  OrderCreatedEvent     → la commande existe maintenant
  OrderShippedEvent     → le colis est parti
  OrderCancelledEvent   → la commande est annulée
  PaymentDeclinedEvent  → le paiement a été refusé

✅ Données dans l'event = ce qui est nécessaire aux handlers
  Pas l'entité complète → juste les champs utiles aux consommateurs
  OrderShippedEvent(OrderId, CustomerId, TrackingNumber, ShippedAt)
```

## Checklist avant de créer un handler

- [ ] C'est une Command OU une Query — pas les deux
- [ ] Le nom exprime l'intention métier (verbe + entité + contexte)
- [ ] Le retour est minimal (ID créé, Result<Unit>, DTO projeté)
- [ ] Les DomainEvents associés sont identifiés
- [ ] Un test peut être écrit sans infrastructure (repo mockable)
