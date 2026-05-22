# Skill: DDD Tactical Patterns

## Quand activer ce skill
- Modélisation d'un nouveau bounded context
- Décision Entity vs ValueObject vs AggregateRoot
- Conception des invariants d'un agrégat
- Design des Specifications

## Pattern: Aggregate boundaries

```
Règle de Evans : une transaction = un agrégat.
Si tu as besoin de modifier deux agrégats dans la même transaction → questionne le design.

Bons signaux d'une frontière correcte :
  ✅ L'agrégat peut valider tous ses invariants sans appel externe
  ✅ On accède toujours à l'agrégat par sa racine (AggregateRoot)
  ✅ Les entités enfants n'ont pas de sens sans leur AggregateRoot

Mauvais signaux :
  ❌ L'agrégat est énorme (50+ propriétés) → trop grand
  ❌ L'agrégat est un wrapper autour d'un seul champ → trop petit
  ❌ Les entités enfants sont référencées directement depuis l'extérieur
```

## Pattern: Value Object vs Entity — cas difficiles

```
Address : VO ou Entity ?
  → Dans un contexte e-commerce : VO (copie snapshot au moment de la commande)
  → Dans un CRM : Entity (l'adresse a un cycle de vie, peut être mise à jour)
  → Réponse : dépend du bounded context

Money : toujours VO
  → Deux Money(10, EUR) sont interchangeables → VO

OrderLine : Entity ou VO ?
  → Si on a besoin d'identifier une ligne précise (modifier qty d'une ligne) → Entity
  → Si les lignes sont recréées à chaque changement → VO
```

## Pattern: Specification composable

```csharp
// Composition fluente
var spec = new ActiveUserSpec()
    .And(new EmailVerifiedSpec())
    .And(new NotBannedSpec());

var users = repository.Find(spec);

// Négation
var inactiveSpec = new ActiveUserSpec().Not();

// Conversion vers Expression<Func<T, bool>> pour EF Core
var expression = spec.ToExpression();
var users = dbContext.Users.Where(expression).ToList();
```

## Pattern: DomainService

```
Quand utiliser un IDomainService ?
  → La logique implique plusieurs agrégats
  → La logique ne peut pas appartenir naturellement à un seul agrégat
  → Exemple : TransferService(from: Account, to: Account, amount: Money)

Quand NE PAS utiliser IDomainService ?
  → La logique appartient à un seul agrégat → méthode sur l'agrégat
  → La logique nécessite de l'infrastructure → Application Service
```

## Pattern: Repository abstraction dans Domain

```csharp
// ✅ Interface dans Domain — implémentation dans Infrastructure
public interface IOrderRepository : IRepository<Order, OrderId>
{
    // Méthodes de requête spécifiques au domaine
    Task<IReadOnlyList<Order>> FindPendingOrdersAsync(CustomerId customerId, CancellationToken ct = default);
}

// ✅ Utilisation dans Application Service (pas dans le domaine)
public sealed class ShipOrderHandler(IOrderRepository orders, IUnitOfWork uow)
{
    public async ValueTask Handle(ShipOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct);
        order.Ship(cmd.TrackingNumber);
        await uow.SaveChangesAsync(ct);
    }
}
```

## Pattern: Exceptions vs Result dans Domain

```
MicroKit.Domain n'a PAS de dépendance sur MicroKit.Result.

Stratégie :
  Violation d'invariant (ne peut pas exister) → throw DomainException / BusinessRuleViolationException
  Résultat prévisible de méthode de recherche → retourner null ou utiliser Result<T> DANS LA COUCHE APPLICATION

Exemples dans le domaine pur :
  order.Ship() → throw si déjà expédié (invariant)
  order.FindItem(id) → retourner null si non trouvé (prévisible)

Exemples dans l'application :
  orderService.ShipAsync(cmd) → Result<Unit> (wrapping du domain)
  orderQuery.GetByIdAsync(id) → Result<OrderDto> (avec NotFoundError)
```
