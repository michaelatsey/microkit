# Rule: C# Style — MicroKit.Domain

## Spécificités Domain (complète le style global MicroKit)

### Types de domaine — sealed par défaut
```csharp
// ✅ Tout type concret = sealed
public sealed class OrderMustHaveItemsRule : BusinessRule { }
public sealed record Money(decimal Amount, string Currency);
public sealed record OrderPlacedEvent(...) : DomainEvent;

// ✅ Abstractions = abstract (non sealed)
public abstract class AggregateRoot<TId> { }
public abstract class ValueObject { }
public abstract class BusinessRule : IBusinessRule { }
```

### IDs — readonly record struct
```csharp
// ✅ Toujours readonly record struct pour les IDs
public readonly record struct OrderId(Guid Value) : IEntityId
{
    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId Empty => new(Guid.Empty);
    public static OrderId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

### DomainEvents — sealed record avec DateTimeOffset
```csharp
// ✅ Toujours sealed record
// ✅ Toujours OccurredAt en DateTimeOffset (jamais DateTime)
// ✅ Toujours l'ID de l'agrégat concerné en premier paramètre
public sealed record OrderShippedEvent(
    OrderId OrderId,
    TrackingNumber Tracking,
    DateTimeOffset OccurredAt) : DomainEvent;
```

### Constructeurs d'agrégats — private + factory method
```csharp
// ✅ Constructeur private pour forcer l'usage de la factory
private Order(OrderId id, CustomerId customerId) : base(id)
{
    CustomerId = customerId;
    Status = OrderStatus.Draft;
    CreatedAt = DateTimeOffset.UtcNow;
}

// ✅ Factory method qui valide et raise l'event de création
public static Order Place(CustomerId customerId, IReadOnlyList<OrderItemRequest> items)
{
    ArgumentNullException.ThrowIfNull(customerId);
    var order = new Order(OrderId.New(), customerId);
    order.CheckRule(new OrderMustHaveItemsRule(items));
    // ajouter les items...
    order.RaiseDomainEvent(new OrderPlacedEvent(order.Id, customerId, DateTimeOffset.UtcNow));
    return order;
}
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
