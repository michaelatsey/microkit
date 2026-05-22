# MicroKit.Domain — Module Brain

## 🎯 Mission
Fournir les primitives DDD fondamentales pour .NET 10+.
Zéro dépendance sur les autres modules MicroKit ou des packages tiers lourds.
Base de tout l'écosystème MicroKit — doit être stable avant tout autre module.

> **Règle absolue :** MicroKit.Domain ne dépend de rien d'autre que le runtime .NET.

---

## 🏛️ Primitives fournies

### Entités et agrégats
```
AggregateRoot<TId>    ← racine d'agrégat, porte les DomainEvents
Entity<TId>           ← entité avec identité, sans events
ValueObject           ← immuable, égalité par valeur
```

### Identifiants
```
IEntityId             ← contrat commun
EntityId<T>           ← strongly-typed ID (record struct)
GuidId                ← shortcut Guid-based ID
```

### Événements de domaine
```
IDomainEvent          ← contrat minimal (OccurredAt, EventId)
DomainEvent           ← base abstraite avec métadonnées
```

### Specifications
```
ISpecification<T>     ← contrat (IsSatisfiedBy, ToExpression)
Specification<T>      ← base avec And/Or/Not composables
AndSpecification<T>
OrSpecification<T>
NotSpecification<T>
```

### Repository abstractions
```
IRepository<T, TId>         ← CRUD de base
IReadRepository<T, TId>     ← lecture seule
IUnitOfWork                 ← transaction abstraction
```

### Domain Services
```
IDomainService              ← marker interface
```

### Exceptions
```
DomainException             ← base de toutes les exceptions domaine
BusinessRuleViolationException(IBusinessRule rule)
EntityNotFoundException<T>(TId id)
```

### Règles métier
```
IBusinessRule               ← contrat (IsBroken, Message)
BusinessRule                ← base abstraite
```

---

## 📐 Modèle objet

### AggregateRoot
```csharp
// Porte les events, contrôle les invariants
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : IEntityId
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> PopDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
```

### Entity
```csharp
// Identité + égalité par ID
public abstract class Entity<TId> where TId : IEntityId
{
    public TId Id { get; protected set; }
    // Equals/GetHashCode basés sur Id
}
```

### ValueObject
```csharp
// Égalité structurelle — utiliser record de préférence
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();
    // Equals/GetHashCode basés sur GetEqualityComponents()
}
// ✅ Alternative moderne recommandée : sealed record directement
```

### IDs fortement typés
```csharp
// Strongly-typed
public readonly record struct OrderId(Guid Value) : IEntityId
{
    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}

// Ou via le type générique fourni
public readonly record struct UserId : IEntityId
{
    // utilise EntityId<UserId> helper
}
```

### Exceptions
```csharp
// Violation d'invariant — throw légitime dans le domaine
public sealed class BusinessRuleViolationException(IBusinessRule rule)
    : DomainException($"Business rule '{rule.GetType().Name}' was violated: {rule.Message}");

// Usage dans un agrégat
protected void CheckRule(IBusinessRule rule)
{
    if (rule.IsBroken())
        throw new BusinessRuleViolationException(rule);
}
```

---

## 🔧 Règles de conception

### Ce qui APPARTIENT à Domain
```
✅ Invariants et règles métier (IBusinessRule)
✅ DomainEvents (faits passés immuables)
✅ Abstractions de repository (interfaces uniquement)
✅ Abstractions IDomainService (marker interface)
✅ Exceptions de domaine (violations d'invariants)
✅ Specifications (logique de sélection/filtrage)
✅ Value objects, entities, aggregates
```

### Ce qui N'APPARTIENT PAS à Domain
```
❌ Implémentation des repositories (appartient à Persistence)
❌ Logique d'envoi d'emails, SMS (appartient à Application)
❌ Accès base de données (appartient à Infrastructure)
❌ Dépendances NuGet tierces (sauf abstractions pures)
❌ ILogger, IMediator, IServiceProvider
❌ HttpContext, DbContext
```

### Immuabilité
```csharp
// ✅ DomainEvent immuable — record sealed
public sealed record OrderCreatedEvent(
    OrderId OrderId,
    Guid CustomerId,
    DateTimeOffset OccurredAt) : DomainEvent;

// ✅ ValueObject immuable — record sealed recommandé
public sealed record Money(decimal Amount, string Currency) : ValueObject
{
    public static Money Zero(string currency) => new(0, currency);
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new DomainException("Cannot add money with different currencies.");
        return new(Amount + other.Amount, Currency);
    }
}
```

---

## 📦 Packages du module

```
MicroKit.Domain                ← tout (pas de split Abstractions pour ce module)
```

> Justification : Domain EST l'abstraction. Créer un `MicroKit.Domain.Abstractions`
> serait une abstraction de l'abstraction — inutile.

---

## 🗂️ Structure src/

```
src/MicroKit.Domain/
├── Aggregates/
│   ├── AggregateRoot.cs
│   └── Entity.cs
├── Events/
│   ├── IDomainEvent.cs
│   └── DomainEvent.cs
├── Exceptions/
│   ├── DomainException.cs
│   ├── BusinessRuleViolationException.cs
│   └── EntityNotFoundException.cs
├── Identifiers/
│   ├── IEntityId.cs
│   ├── EntityId.cs
│   └── GuidId.cs
├── Repositories/
│   ├── IRepository.cs
│   ├── IReadRepository.cs
│   └── IUnitOfWork.cs
├── Rules/
│   ├── IBusinessRule.cs
│   └── BusinessRule.cs
├── Services/
│   └── IDomainService.cs
├── Specifications/
│   ├── ISpecification.cs
│   ├── Specification.cs
│   ├── AndSpecification.cs
│   ├── OrSpecification.cs
│   └── NotSpecification.cs
├── ValueObjects/
│   └── ValueObject.cs
└── GlobalUsings.cs
```

---

## 🔗 Références
- [Domain-Driven Design — Eric Evans]
- [Implementing DDD — Vaughn Vernon]
- [DDD with .NET — Milan Jovanović](https://milan.milanovic.org)
