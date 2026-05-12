# MicroKit.Domain.Contracts

Pure interface contracts for DDD primitives. No external dependencies, no implementations — only the minimal interface surface that infrastructure packages (persistence, event dispatch, repositories) need to reference domain types without coupling to concrete domain classes.

## When to use

- Reference this in EF Core configurations, repository implementations, domain event dispatchers, and any other infrastructure package that must interact with entities or events without depending on `MicroKit.Domain.Abstractions`.
- Reference `MicroKit.Domain.Abstractions` in your domain layer for the actual base classes your aggregates and entities inherit from.

## Installation

```
dotnet add package MicroKit.Domain.Contracts
```

## Key types

| Type | Description |
|---|---|
| `IEntity` | Exposes `GetKeys()` — composite key support without a typed `Id` |
| `IEntity<TKey>` | Typed `Id` property in addition to `GetKeys()` |
| `IAggregateRoot` | Extends `IEntity` and `IHasDomainEvents`; adds `Version` and `RowVersion` for optimistic concurrency |
| `IAggregateRoot<TKey>` | Typed variant of `IAggregateRoot` |
| `IHasDomainEvents` | `IReadOnlyCollection<IDomainEvent>? DomainEvents` and `ClearDomainEvents()` |
| `IDomainEvent` | `Guid Id` and `DateTimeOffset OccurredOnUtc` |
| `IAuditedEntity` | `CreatedOnUtc`, `CreatedBy`, `LastModifiedOnUtc`, `LastModifiedBy` |

## Usage

```csharp
// Infrastructure: EF Core interceptor that dispatches domain events
public class DomainEventDispatchInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct)
    {
        var aggregates = eventData.Context!.ChangeTracker
            .Entries<IAggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents?.Any() == true)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            // dispatch events...
            aggregate.ClearDomainEvents();
        }

        return result;
    }
}
```

## Dependencies

None.
