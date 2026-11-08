# Context: Ecosystem Overview

**How MicroKit.Persistence fits in the broader MicroKit monorepo.**

---

## Position in the Dependency Graph

```
Level 0 (foundations):
  MicroKit.Domain        ← IAggregateRoot, Specification<T>, IDomainEvent, value objects
  MicroKit.Result        ← Result<T>, Error, Unit

Level 1:
  MicroKit.Logging       → (Result abstained — ADR-006 in Logging module)
  MicroKit.Caching       → Result

Level 2:
  MicroKit.MediatR       → Result, Domain.Abstractions, Logging.Abstractions
  MicroKit.Persistence   → Result, Domain.Abstractions, Logging.Abstractions

Level 3:
  MicroKit.Messaging     → Result, Domain, Persistence (outbox pattern)
  MicroKit.Http          → Result, Observability
  MicroKit.Multitenancy  → Result, Auth, Persistence
```

MicroKit.Persistence sits at **Level 2**, alongside MicroKit.MediatR. Both modules depend on the
same Level 0 foundations but are otherwise independent — neither depends on the other directly.
The integration between them is achieved via `ITransactionalContext` (declared in
Persistence.Abstractions, consumed by MediatR.Behaviors).

---

## Who Consumes MicroKit.Persistence

| Consumer | What they use |
|----------|--------------|
| Application layer (command handlers) | `IRepository<TAggregate, TId>`, `IUnitOfWork`, `ITransactionalContext` |
| Application layer (query handlers) | `IReadRepository<T>`, `QueryOptions<T>`, `IPagedResult<T>` |
| `MicroKit.MediatR.Behaviors` | `ITransactionalContext` (for `TransactionBehavior`) |
| `MicroKit.Messaging` (future) | `IRepository<TAggregate, TId>`, `IUnitOfWork` (outbox pattern) |
| `MicroKit.Multitenancy` (future) | `IReadRepository<T>` (tenant resolution) |
| Test projects | `InMemoryRepository<TAggregate, TId>`, `InMemoryUnitOfWork` from `Persistence.Testing` |

---

## What MicroKit.Persistence Does NOT Own

| Concern | Owner |
|---------|-------|
| `Specification<T>` (predicate) | `MicroKit.Domain` |
| `IAggregateRoot` | `MicroKit.Domain.Abstractions` |
| `IDomainEvent` / domain event dispatch | `MicroKit.MediatR` |
| Caching behavior in the pipeline | `MicroKit.MediatR.Behaviors` (CachingBehavior) |
| Connection string management | Consuming application |
| Migration execution | Consuming application or dedicated migration project |

---

## Integration Points

### With MicroKit.MediatR

```
MicroKit.MediatR.Behaviors.TransactionBehavior
    ← injects ITransactionalContext (from MicroKit.Persistence.Abstractions)
    ← wraps ICommand handlers in a DB transaction automatically
    ← consumers opt in by registering both modules and the TransactionBehavior
```

See `.claude-context/context/transaction-behavior-integration.md` for full design.

### With MicroKit.Messaging (future — Level 3)

The outbox pattern requires:
1. A `StoredMessage` aggregate persisted via `IRepository<StoredMessage>`
2. A `IUnitOfWork` that commits both the business aggregate and the outbox entry atomically
3. `ITransactionalContext` for the cross-aggregate transaction

The Messaging module will depend on `Persistence.Abstractions` for these contracts.

### With MicroKit.Domain

```
MicroKit.Domain provides:
  IAggregateRoot      ← generic constraint on IRepository<TAggregate, TId>
  Specification<T>    ← WHAT to query, wrapped by QueryOptions<T>

MicroKit.Persistence provides:
  IRepository<TAggregate, TId>      ← wraps IAggregateRoot for write-side persistence
  QueryOptions<T>     ← HOW to execute a Specification<T>
```

---

## Package Adoption Guidance for Consumers

| Scenario | Packages needed |
|----------|----------------|
| Define repository contracts only | `MicroKit.Persistence.Abstractions` |
| Use QueryOptions in handlers | + `MicroKit.Persistence` (core) |
| Wire EF Core | + `MicroKit.Persistence.EntityFrameworkCore` |
| Use PostgreSQL | + `MicroKit.Persistence.EntityFrameworkCore.PostgreSql` |
| Use SQL Server | + `MicroKit.Persistence.EntityFrameworkCore.SqlServer` |
| Write spec extensions | + `MicroKit.Persistence.Specifications` |
| Write repository tests | + `MicroKit.Persistence.Testing` |
| Enforce design rules at build time | + `MicroKit.Persistence.Analyzers` (PrivateAssets=all) |
