# MicroKit.Domain.Abstractions

Abstract base classes for DDD building blocks. Provides concrete (but abstract) implementations of `Entity`, `AggregateRoot`, `ValueObject`, `DomainEvent`, `Enumeration`, and their audited variants. All implementations satisfy the contracts defined in `MicroKit.Domain.Contracts`.

## When to use

- Reference `MicroKit.Domain.Abstractions` in your domain projects — your aggregates and entities inherit from these base classes.
- Reference `MicroKit.Domain.Contracts` (no implementations) in infrastructure packages (EF Core configurations, repositories, dispatchers) that must reference domain types without depending on concrete classes.
- Reference `MicroKit.Domain` for `Result<T>`, domain exceptions, and built-in value objects such as `Money`.

## Installation

```
dotnet add package MicroKit.Domain.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `Entity` / `Entity<TKey>` | Base entity with composite-key support via `GetKeys()`; rejects default key values |
| `AggregateRootBase` / `AggregateRootBase<TKey>` | Manages domain event collection, `Version` counter, and `RowVersion` for optimistic concurrency |
| `AggregateRoot` / `AggregateRoot<TKey>` | Thin convenience wrappers over `AggregateRootBase`; use these as the direct base class |
| `AuditedEntity<TKey>` | Adds `CreatedOnUtc`, `CreatedBy`, `LastModifiedOnUtc`, `LastModifiedBy` to entities |
| `AuditedAggregateRoot` / `AuditedAggregateRoot<TKey>` | Aggregate root with full audit trail; exposes `UpdateTimestamp()` for domain methods |
| `DomainEvent` | Abstract record base with `Guid Id` and `DateTimeOffset OccurredOnUtc`; auto-assigned in constructor |
| `ValueObject` | Structural equality via `GetAtomicValues()`; implement to define immutable value types |
| `Enumeration` | Rich enum base with `Id`, `Name`, `DisplayName`, `Description`; `GetAll<T>()`, `FromId<T>()`, `FromName<T>()` |

## Usage

```csharp
// Aggregate root
public class Order : AggregateRoot<Guid>
{
    public OrderStatus Status { get; private set; }

    public Order(Guid id) : base(id) { }

    public void Confirm()
    {
        Status = OrderStatus.Confirmed;
        IncrementVersion();
        AddDomainEvent(new OrderConfirmed(Id));
    }
}

// Domain event
public sealed record OrderConfirmed(Guid OrderId) : DomainEvent;

// Value object
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

## Dependencies

- `MicroKit.Domain.Contracts`
