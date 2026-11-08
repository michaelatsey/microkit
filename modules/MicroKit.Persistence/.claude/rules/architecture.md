# Rule: Architecture — MicroKit.Persistence

Applies to every file in this module.

## Write side — `IRepository<TAggregate, TId>` (Abstractions)

```csharp
public interface IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot
    where TId : IEntityId
{
    ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default); // whole aggregate, tracked
    ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);
    ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);
}

// ✅ A typed repository extends the contract; the EF implementation derives from EfRepository
public interface IOrderRepository : IRepository<Order, OrderId>
{
    ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default);
}

// ❌ A repository for a child entity — only aggregate roots have repositories
public interface IOrderLineRepository : IRepository<OrderLine, OrderLineId> { }
```

- No `CommitAsync`: committing belongs to `IUnitOfWork` only.
- No `UpdateAsync`: an aggregate loaded through `FindAsync` is tracked; mutate it and commit.
- `EfRepository.FindAsync` returns an instance already tracked as is; otherwise it loads the whole
  aggregate (every navigation inside the boundary, never another aggregate root), split into
  separate queries when it has two or more collections. A typed repository narrows or extends
  the load by overriding `protected virtual IncludeAggregate`.

## Read side — `IReadRepository<TAggregate>` (core)

```csharp
public interface IReadRepository<TAggregate> where TAggregate : class, IAggregateRoot
{
    ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default);
    ValueTask<IReadOnlyList<TAggregate>> ListAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<IPagedResult<TAggregate>> ListPagedAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
    ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default);
}
```

It is the only read contract (it lives in core because it takes `QueryOptions<T>`). It never
mutates or commits (MKP001/MKP002). `EfReadRepository<TAggregate, TContext>(context, evaluator,
paginationLimits)` implements it; a typed read repository derives from it.

## Unit of Work — `IUnitOfWork` (Abstractions)

```csharp
public interface IUnitOfWork
{
    ValueTask CommitAsync(CancellationToken ct = default);  // throws PersistenceException
    void DiscardChanges();  // abandons the change set, no I/O (EF Core: ChangeTracker.Clear) — ADR-005
}
```

`DiscardChanges` is called by `TransactionBehavior` on every non-commit exit, not by handlers.

## Transactions — `ITransactionalContext` (Abstractions)

```csharp
public interface ITransactionalContext
{
    Task ExecuteAsync<TState>(Func<TState, CancellationToken, Task> operation, TState state, CancellationToken ct = default);
    Task<TResult> ExecuteAsync<TState, TResult>(Func<TState, CancellationToken, Task<TResult>> operation, TState state, CancellationToken ct = default);
}
```

There is no Begin/Commit/Rollback on the contract: the implementation runs the operation inside
the provider's execution strategy and may re-invoke it. `EfUnitOfWork<TContext>` (ADR-006):

- joins a transaction already open on the context — no strategy, no begin, commit or rollback;
- otherwise begins a transaction per attempt, retries what the strategy classifies as transient
  (at-least-once: keep retries off for non-idempotent operations), and surfaces only
  `PersistenceException`;
- rolls back with `CancellationToken.None`, swallows rollback/dispose failures so the original
  exception survives, and clears the change tracker after a failed attempt.

`ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext` is the EF Core composite and stays
out of Abstractions (ADR-004).

`TransactionBehavior` (MicroKit.MediatR.Behaviors) wraps command handlers in `ExecuteAsync`; on
success it dispatches domain events and then calls `CommitAsync` (the flush must follow the
dispatch, or the outbox rows it staged are lost); on failure or exception it calls `DiscardChanges`.

## QueryOptions — WHAT vs HOW (ADR-002)

```csharp
var opts = new QueryOptions<Order>(new OpenOrdersSpec())   // WHAT — Specification<T> from Domain
    .WithIncludes(q => q.Include(o => o.Lines))            // HOW  — core extensions
    .WithOrderByDescending(o => o.CreatedAt)               //        MicroKit.Persistence.Specifications
    .WithPagination(page: 1, pageSize: 20);
```

Untracked by default (`AsNoTrackingEnabled = true`); `WithTracking()` opts out.

## CQRS Data-Access Alignment

| Handler | Inject | Forbidden |
|---|---|---|
| Command | `IRepository<TAggregate, TId>` (or a typed one) + `IUnitOfWork` | `IReadRepository`, `DbContext` |
| Query | `IReadRepository<T>` (or a typed one) | `IRepository`, `IUnitOfWork`, `DbContext` |

## Strict Rules

```
🔴 IReadRepository with AddAsync / UpdateAsync / DeleteAsync / CommitAsync
🔴 A repository with CommitAsync or SaveChangesAsync — commit through IUnitOfWork
🔴 DbContext injected in a handler (MKP004 heuristic)
🔴 IQueryable<T> returned by a repository method (MKP005)
🔴 Parameterless IgnoreQueryFilters() in library code (IlCallScanTests)
🟡 IUnitOfWork referenced from MicroKit.Domain — it lives in Persistence.Abstractions (ADR-001)
```
