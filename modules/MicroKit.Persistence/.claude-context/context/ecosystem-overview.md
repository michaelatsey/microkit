# Context: Ecosystem Overview

How MicroKit.Persistence fits among the MicroKit modules. The authoritative graph is the root
`.claude/CLAUDE.md`; edges as declared: `dependency-graph.md`.

---

## Position

```
MicroKit.Domain        ← IAggregateRoot, IEntityId/EntityId<T>, Specification<T>, domain events,
                         Actor, IAuditableEntity
    ↑
MicroKit.Persistence   ← repositories, unit of work, transactions, QueryOptions, EF Core bridge
    ↑
MicroKit.MediatR.Behaviors            TransactionBehavior over ITransactionalContext + IUnitOfWork
MicroKit.Tenancy.EntityFrameworkCore  tenant-aware EF Core integration
```

Persistence depends on MicroKit.Domain (Abstractions) and MicroKit.Result (core, for the `Result`
path of ADR-011). It does not depend on Logging, MediatR, Messaging or Tenancy.

---

## Who consumes what

| Consumer | Uses |
|----------|------|
| Command handlers | `IRepository<TAggregate, TId>` (or a typed one), `IUnitOfWork` |
| Query handlers | `IReadRepository<T>`, `QueryOptions<T>`, `IPagedResult<T>` |
| Composition root | `AddMicroKitPersistence` → `AddEntityFrameworkCore` → `UsePostgreSQL`/`UseSqlServer`, `AddUnitOfWork`, `ConfigurePagination` |
| `DbContext.ConfigureConventions` | `AddDomainConventions` |
| `MicroKit.MediatR.Behaviors` | `ITransactionalContext`, `IUnitOfWork` (`TransactionBehavior`) |
| `MicroKit.Tenancy.EntityFrameworkCore` | Abstractions + EntityFrameworkCore |
| Test projects | `InMemoryRepository<TAggregate, TId>`, `InMemoryReadRepository<T>`, `InMemoryUnitOfWork` |

---

## What Persistence does not own

| Concern | Owner |
|---------|-------|
| `Specification<T>`, `IAggregateRoot`, identifiers, `Actor` | MicroKit.Domain |
| Domain event dispatch | MicroKit.MediatR (`IDomainEventsDispatcher`); Persistence only provides the tracked aggregates' events (`EfDomainEventsProvider`) |
| The transaction pipeline | MicroKit.MediatR.Behaviors (`TransactionBehavior`) |
| Tenant filters | MicroKit.Tenancy — Persistence never disables query filters wholesale |
| Connection strings, migrations | The consuming application |

---

## Integration with MicroKit.MediatR

`TransactionBehavior` wraps a command in `ITransactionalContext.ExecuteAsync`; on success it
dispatches domain events, then calls `IUnitOfWork.CommitAsync`; on a business failure or an
exception it calls `IUnitOfWork.DiscardChanges()` (ADR-005). `EfUnitOfWork` retries the whole
operation on a transient failure and joins an already open transaction (ADR-006). ADR-006 records
the consequence for nested command dispatch: an inner discard clears the outer command's staged
changes — a pipeline concern this module leaves to MediatR.

---

## Package adoption

| Scenario | Packages |
|----------|----------|
| Contracts only | `MicroKit.Persistence.Abstractions` |
| Read side, QueryOptions | + `MicroKit.Persistence` |
| EF Core | + `MicroKit.Persistence.EntityFrameworkCore` |
| PostgreSQL / SQL Server | + `.EntityFrameworkCore.PostgreSql` / `.EntityFrameworkCore.SqlServer` |
| Ordering and spec-swap helpers | + `MicroKit.Persistence.Specifications` |
| Handler unit tests | + `MicroKit.Persistence.Testing` |
| Build-time rules | + `MicroKit.Persistence.Analyzers` (`PrivateAssets=all`) |
