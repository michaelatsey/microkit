# Rule: C# Style — MicroKit.Domain

## Spécificités Domain (complète le style global MicroKit)

### Types de domaine — sealed par défaut
```csharp
// ✅ Tout type concret = sealed
public sealed class OrderMustHaveItemsRule : BusinessRule { }
public sealed record Money(decimal Amount, Currency Currency) : IValueObject;
public sealed record OrderPlacedEvent(OrderId OrderId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

// ✅ Abstractions = abstract (non sealed)
public abstract class AggregateRoot<TId> { }
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent { }
public abstract class BusinessRule : IBusinessRule { }
```

### IDs — readonly record struct
```csharp
// ✅ Toujours readonly record struct pour les IDs
// ✅ UUID v7 (ordonné dans le temps) ; pas de fabrique Empty : Entity<TId> rejette default(TId)
public readonly record struct OrderId(Guid Value) : IEntityId
{
    public static OrderId New() => new(Guid.CreateVersion7());
    public static OrderId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

### DomainEvents — sealed record avec DateTimeOffset
```csharp
// ✅ Toujours sealed record
// ✅ Toujours OccurredAt en DateTimeOffset (jamais DateTime), dernier paramètre, transmis à la base
// ✅ Toujours l'ID de l'agrégat concerné en premier paramètre
public sealed record OrderShippedEvent(
    OrderId OrderId,
    TrackingNumber Tracking,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
```

### Constructeurs d'agrégats — private + factory method
```csharp
// ✅ Constructeur private pour forcer l'usage de la factory
// ✅ L'instant est une entrée de l'opération, jamais une horloge ambiante
private Order(OrderId id, CustomerId customerId, DateTimeOffset createdAt) : base(id)
{
    CustomerId = customerId;
    Status = OrderStatus.Draft;
    CreatedAt = createdAt;
}

// ✅ Factory method qui valide et raise l'event de création
public static Order Place(CustomerId customerId, IReadOnlyList<OrderItemRequest> items, DateTimeOffset now)
{
    ArgumentNullException.ThrowIfNull(items);
    var order = new Order(OrderId.New(), customerId, now);
    order.CheckRule(new OrderMustHaveItemsRule(items.Count));
    // ajouter les items...
    order.RaiseDomainEvent(new OrderPlacedEvent(order.Id, now));
    return order;
}

// ✅ Agrégat audité : TimeProvider et Actor à la création, Touch à chaque mutation
private Invoice(InvoiceId id, TimeProvider clock, Actor createdBy) : base(id, clock, createdBy) { }
```

### Collections dans les agrégats
```csharp
// ✅ Backing field privé + propriété read-only
private readonly List<OrderItem> _items = [];
public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

// ❌ Propriété publique mutable
public List<OrderItem> Items { get; set; } = []; // ❌
```

### Ordering des membres dans un agrégat
1. Constants / static readonly
2. Backing fields privés (`_items`, `_events`)
3. Constructeur private
4. Factory methods statiques
5. Propriétés publiques (init-only ou private set)
6. Méthodes publiques (commandes domaine)
7. Méthodes privées helpers
8. `CheckRule` overrides si nécessaire
