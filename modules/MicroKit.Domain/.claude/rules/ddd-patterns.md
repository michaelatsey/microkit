# Rule: DDD Patterns — MicroKit.Domain

## Toujours actif pour tout fichier dans ce module.

## Hiérarchie des types

```
AggregateRoot<TId>   ← racine, porte les events, frontière de cohérence
  └── Entity<TId>   ← entité enfant, accessible uniquement via son agrégat
ValueObject          ← immuable, égalité par valeur, pas d'identité
IDomainEvent         ← fait passé, immuable, sealed record
IBusinessRule        ← invariant vérifiable, booléen
ISpecification<T>    ← critère de sélection, composable
```

## Règles par type

### AggregateRoot
```csharp
// ✅ Constructeur private — factory method obligatoire
private Order(OrderId id, CustomerId customerId) : base(id) { }
public static Order Place(CustomerId customerId, IEnumerable<OrderItem> items)
{
    var order = new Order(OrderId.New(), customerId);
    order.CheckRule(new OrderMustHaveItemsRule(items));
    order.RaiseDomainEvent(new OrderPlacedEvent(order.Id, DateTimeOffset.UtcNow));
    return order;
}

// ✅ RaiseDomainEvent APRÈS mutation d'état
public void Ship(TrackingNumber tracking)
{
    CheckRule(new OrderCanBeShippedRule(Status));
    Status = OrderStatus.Shipped;           // mutation d'abord
    TrackingNumber = tracking;
    RaiseDomainEvent(new OrderShippedEvent(Id, tracking, DateTimeOffset.UtcNow)); // event après
}

// ❌ Accès direct aux entités enfants depuis l'extérieur
order.Items.Add(new OrderItem(...));   // ❌ collection publique mutable
// ✅
order.AddItem(productId, quantity);   // méthode sur l'agrégat
```

### Entity
```csharp
// ✅ Égalité par ID
public abstract class Entity<TId> where TId : IEntityId
{
    public TId Id { get; protected init; }  // init-only
    public override bool Equals(object? obj) => obj is Entity<TId> other && Id.Equals(other.Id);
    public override int GetHashCode() => Id.GetHashCode();
}

// ✅ Propriétés avec private set ou init
public string Name { get; private set; }
public DateTimeOffset CreatedAt { get; init; }
```

### ValueObject
```csharp
// ✅ Préférer sealed record (moderne, égalité structurelle automatique)
public sealed record Money(decimal Amount, string Currency)
{
    // Validation dans le constructeur
    public Money
    {
        if (Amount < 0) throw new DomainException("Amount cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
}

// ✅ Opérations retournent de nouvelles instances (immuabilité)
public Money Add(Money other) => this with { Amount = Amount + other.Amount };
```

### DomainEvent
```csharp
// ✅ sealed record — toujours
public sealed record OrderPlacedEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    DateTimeOffset OccurredAt) : DomainEvent;

// ❌ class mutable
public class OrderPlacedEvent { public Guid OrderId { get; set; } } // ❌
// ❌ DateTimeOffset absent ou DateTime
public sealed record OrderPlacedEvent(Guid OrderId); // ❌ manque OccurredAt
```

### BusinessRule
```csharp
// ✅ Une règle = un invariant précis
public sealed class OrderMustHaveItemsRule(IEnumerable<OrderItem> items) : BusinessRule
{
    public override bool IsBroken() => !items.Any();
    public override string Message => "An order must contain at least one item.";
}

// ✅ Utilisation dans l'agrégat
protected void CheckRule(IBusinessRule rule)
{
    if (rule.IsBroken())
        throw new BusinessRuleViolationException(rule);
}
```

### IDs fortement typés
```csharp
// ✅ readonly record struct — value semantics, 0 allocation
public readonly record struct OrderId(Guid Value) : IEntityId
{
    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId Empty => new(Guid.Empty);
    public static OrderId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

// ❌ Guid nu dans les signatures publiques
public void AssignTo(Guid userId) { } // ❌
// ✅
public void AssignTo(UserId userId) { }
```

## Anti-patterns stricts

```
❌ Agrégat qui appelle un service (IEmailService, IRepository)
❌ DomainEvent avec setter ou propriété mutable
❌ ValueObject avec identité (Guid dans un VO = Entity mal classé)
❌ Logique d'application dans le domaine (orchestration, workflow)
❌ Exception d'infrastructure (HttpRequestException, SqlException) dans le domaine
❌ Accès à DateTime.Now directement — passer par paramètre ou IDateTimeProvider
```
