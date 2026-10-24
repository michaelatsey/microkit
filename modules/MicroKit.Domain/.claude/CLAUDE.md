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
AggregateRoot<TId>           ← racine d'agrégat, porte les DomainEvents
Entity<TId>                  ← entité avec identité, sans events
IValueObject                 ← marker interface pour contraintes génériques
sealed record                ← value objects complexes (Money, Address, Email)
readonly record struct       ← value objects simples (Percentage, identifiants)
```

### Identifiants
```
IEntityId             ← marqueur de contrainte, sans membre (ADR-003 D2)
EntityId<T>           ← base abstract record d'ID Guid, rejette Guid.Empty
GuidId                ← readonly record struct Guid ; New() en UUID v7
```

### Événements de domaine
```
IDomainEvent          ← contrat minimal (OccurredAt, EventId)
DomainEvent           ← abstract record DomainEvent(DateTimeOffset OccurredAt), EventId en UUID v7
```

### Specifications
```
ISpecification<T>     ← contrat (IsSatisfiedBy, ToExpression)
Specification<T>      ← base avec And/Or/Not composables
AndSpecification<T>
OrSpecification<T>
NotSpecification<T>
```

### Repositories
```
Aucun dans ce module (ADR-003 D1) : MicroKit.Persistence.Abstractions porte
IRepository<TAggregate> et IUnitOfWork.
```

### Domain Services
```
IDomainService              ← marker interface
```

### Exceptions
```
DomainException             ← base de toutes les exceptions domaine
BusinessRuleViolationException(IBusinessRule rule)
EntityNotFoundException<TEntity, TId>(TId entityId)
```

### Règles métier
```
IBusinessRule               ← contrat (IsBroken, Message, Code)
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

    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        var events = _domainEvents.ToArray();
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
    public TId Id { get; protected init; }
    // Le constructeur rejette un Id null et default(TId)
    // Equals : Id égaux ET un type d'exécution assignable à l'autre (proxies EF Core)
    // GetHashCode basé sur Id
}
```

### ValueObject (2026 Modern Approach)
```csharp
// ✅ RECOMMANDÉ: sealed record pour les value objects complexes
// Exemple de motif : le Money livré (ValueObjects/Common/Money.cs) tient un Currency ISO 4217
public sealed record Money(decimal Amount, string Currency) : IValueObject
{
    // Validation dans le constructeur
    public Money {
        if (Amount < 0) throw new DomainException("Amount cannot be negative");
        ArgumentException.ThrowIfNullOrWhiteSpace(Currency);
    }
    
    // Méthodes métier
    public Money Add(Money other) => new(Amount + other.Amount, Currency);
}

// ✅ RECOMMANDÉ: readonly record struct pour les value objects simples
public readonly record struct Percentage(decimal Value) : IValueObject
{
    public Percentage {
        if (Value < 0 || Value > 100) 
            throw new DomainException("Percentage must be 0-100");
    }
}

// ❌ OBSOLÈTE: Abstract base class (removed in 2026)
// public abstract class ValueObject { ... }
```

### IDs fortement typés
```csharp
// Strongly-typed
public readonly record struct OrderId(Guid Value) : IEntityId
{
    public static OrderId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

// Ou via la base générique fournie (record classe, rejette Guid.Empty)
public sealed record UserId : EntityId<UserId>
{
    public UserId(Guid value) : base(value) { }
}
```

### Exceptions
```csharp
// Violation d'invariant — throw légitime dans le domaine
public sealed class BusinessRuleViolationException(IBusinessRule rule)
    : DomainException($"Business rule '{rule.Code}' was violated: {rule.Message}");  // expose Code

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
✅ Abstractions IDomainService (marker interface)
✅ Exceptions de domaine (violations d'invariants)
✅ Specifications (logique de sélection/filtrage)
✅ Value objects, entities, aggregates
```

### Ce qui N'APPARTIENT PAS à Domain
```
❌ Contrats et implémentations de repository / unit of work (appartiennent à Persistence)
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
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

// ✅ ValueObject immuable — record sealed recommandé
// Exemple de motif : le Money livré (ValueObjects/Common/Money.cs) tient un Currency ISO 4217
public sealed record Money(decimal Amount, string Currency) : IValueObject
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
│   ├── AuditRules.cs            ← internal : règles d'audit partagées (ADR-003 D5)
│   ├── AuditableAggregateRoot.cs
│   ├── AuditableEntity.cs
│   ├── Entity.cs
│   ├── IAggregateRoot.cs
│   ├── IAuditableEntity.cs
│   └── IEventJournal.cs         ← internal : retrait des events d'un Mutate qui échoue
├── Events/
│   ├── DomainEvent.cs
│   ├── IDomainEvent.cs
│   ├── IDomainEventsProvider.cs
│   ├── IEvent.cs
│   └── IHasDomainEvents.cs
├── Exceptions/
│   ├── BusinessRuleViolationException.cs
│   ├── DomainException.cs
│   └── EntityNotFoundException.cs
├── Identifiers/
│   ├── EntityId.cs
│   ├── GuidId.cs
│   └── IEntityId.cs
├── Rules/
│   ├── BusinessRule.cs
│   └── IBusinessRule.cs
├── Services/
│   └── IDomainService.cs
├── Specifications/
│   ├── AndSpecification.cs
│   ├── ISpecification.cs
│   ├── NotSpecification.cs
│   ├── OrSpecification.cs
│   ├── ReplaceExpressionVisitor.cs
│   └── Specification.cs
├── ValueObjects/
│   ├── IValueObject.cs
│   └── Common/          ← Actor, Address, Currency, DateRange, Email, FullName,
│       │                   Money, Percentage, PhoneNumber
│       └── Json/        ← ActorJsonConverter, CurrencyJsonConverter, MoneyJsonConverter
│                           (internal), TrustedActorJsonConverter (public, ADR-004 D1)
├── GlobalUsings.cs
└── UtcInstant.cs        ← internal : offset zéro, jamais default (instants d'audit et d'events)
```

---

## 🔗 Références
- [Domain-Driven Design — Eric Evans]
- [Implementing DDD — Vaughn Vernon]
- [DDD with .NET — Milan Jovanović](https://milan.milanovic.org)
