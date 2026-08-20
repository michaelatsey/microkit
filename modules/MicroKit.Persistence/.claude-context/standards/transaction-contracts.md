# Standard: Transaction Contracts

**Canonical design for the transaction layer in MicroKit.Persistence.**

---

## Contract Hierarchy

```
IUnitOfWork                    ← commit / discard boundary (Abstractions)
ITransactionalContext          ← transactional execution (Abstractions)
ITransactionalUnitOfWork       ← composite for EF Core (EntityFrameworkCore — NOT Abstractions)
  : IUnitOfWork, ITransactionalContext
```

---

## IUnitOfWork — the change-set boundary

A unit of work accumulates a pending change set and then either **commits** it or **discards** it.
Both exits are on the contract (ADR-005):

```csharp
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all pending changes accumulated since the last commit or since the
    /// beginning of the current ambient transaction.
    /// </summary>
    ValueTask CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Abandons every pending change accumulated since the last commit, without writing them.
    /// </summary>
    void DiscardChanges();
}
```

**`CommitAsync` invariants:**
- One `CommitAsync()` per command boundary
- Called AFTER all domain logic, staging operations (`AddAsync`, `UpdateAsync`, …), **and** after
  domain-event dispatch — the dispatch stages outbox rows that this flush must carry
- Translates to a single `SaveChangesAsync()` under EF Core
- Throws `PersistenceException` on provider failure (concurrency, constraint violation)

**`DiscardChanges` invariants:**
- Called at every command boundary that does **not** commit — business failure *and* thrown
  exception. EF Core's transaction rollback does not reset the change tracker, so the exception
  path needs the discard exactly as much as the failure path
- Synchronous: no implementation performs I/O. EF Core drops change-tracker references
  (`ChangeTracker.Clear()`); a provider with no pending change set (Dapper, raw SQL) is a no-op
- Discards the whole context's pending set, not one command's entities. Entity references held
  across the call become detached: the in-memory object graph is left intact, but the provider no
  longer tracks it, so re-saving such a reference later inserts a duplicate. **Detached does not
  mean "throws on access"** — verified on EF Core 10.0.9, lazy and explicit navigation loads on a
  detached entity succeed silently by issuing a fresh query, which inside a failing command means
  a round-trip on a transaction that is about to roll back. ADR-005 asserted the opposite until it
  was corrected; do not restate the old claim
- Called by `TransactionBehavior`, **never** by a command handler — a handler cannot know whether
  its scope holds one command or twenty

> **Two different `CommitAsync` methods — do not conflate them.**
> `IUnitOfWork.CommitAsync` is the **flush** (`SaveChangesAsync`) — it is what writes rows.
> The database transaction commit is `IDbContextTransaction.CommitAsync`, performed internally by
> `ITransactionalContext`. Committing the transaction without flushing commits an empty transaction
> and writes nothing. This homonym caused a silent no-write defect (#78); it is also why the discard
> member is named `DiscardChanges` and not `Rollback` (ADR-005 §8).

---

## ITransactionalContext — transactional execution

Begin, commit, and rollback are managed **internally** by the implementation. The contract exposes
execution, not lifecycle:

```csharp
public interface ITransactionalContext
{
    /// <summary>Executes <paramref name="operation"/> inside a database transaction.</summary>
    Task ExecuteAsync<TState>(
        Func<TState, CancellationToken, Task> operation,
        TState state,
        CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction and returns its result.
    /// </summary>
    Task<TResult> ExecuteAsync<TState, TResult>(
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken ct = default);
}
```

**Why `TState` rather than a closure:** threading caller-owned state through the call keeps the hot
path allocation-free. Combined with a `static` lambda, the JIT specializes on the state type and
neither a display class nor a boxed carrier is allocated.

**Why no `Begin`/`Commit`/`Rollback`:** the implementation wraps the operation in the provider's
execution strategy, which may **re-invoke** it on a transient failure. A contract that handed the
transaction to the caller could not retry safely.

---

## ITransactionalUnitOfWork — EF Core Composite

NOT in Abstractions — lives in `MicroKit.Persistence.EntityFrameworkCore` (ADR-004):

```csharp
public interface ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext { }
```

**Why composite:** `EfUnitOfWork<TContext>` wraps a `DbContext` and implements both sides —
`SaveChangesAsync`/`ChangeTracker.Clear()` for the unit of work, `Database.BeginTransactionAsync`
for transactional execution. The composite exists for the DI registration; consumers inject the
narrowest interface they need and never the composite.

**Why not in Abstractions:** the coupling is EF-specific. A Dapper or Marten provider implements
`IUnitOfWork` and `ITransactionalContext` separately, with no composite.

**DI registration** — `AddUnitOfWork<TContext>()` extends `EfCoreBuilder`, not `IServiceCollection`:

```csharp
services.AddMicroKitPersistence(p => p
    .AddEntityFrameworkCore()
    .AddDbContext<AppDbContext>(o => o.UseNpgsql(cs))   // any EF Core provider
    .AddUnitOfWork<AppDbContext>());
```

which registers one scoped `EfUnitOfWork<TContext>` behind three interface pointers, plus the
change-tracker-backed domain-events provider:

```csharp
builder.Services.AddScoped<EfUnitOfWork<TContext>>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
builder.Services.AddScoped<ITransactionalContext>(sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
builder.Services.AddScoped<ITransactionalUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
builder.Services.AddScoped<IDomainEventsProvider, EfDomainEventsProvider<TContext>>();
```

One instance means the flush targets the **same `DbContext`** the transaction was opened on.

---

## ITransaction

```csharp
public interface ITransaction : IAsyncDisposable
{
    Guid TransactionId { get; }
}
```

Wraps the underlying provider transaction (`IDbContextTransaction` in EF Core) for correlation and
logging. `EfTransaction` is the only implementation.

> Not on any active call path: `ITransactionalContext` manages its transaction internally and never
> surfaces an `ITransaction`. `EfTransaction`'s constructor is `internal` and nothing constructs it.

---

## TransactionBehavior Integration

`TransactionBehavior` (pipeline order 700) in `MicroKit.MediatR.Behaviors` wraps `ICommand` and
`ICommand<TResult>` handlers. Queries, events, and non-command requests pass through untouched.
It injects `ITransactionalContext`, `IDomainEventsDispatcher`, and `IUnitOfWork` — all resolved from
this module.

Sequence for a command:

```
ITransactionalContext.ExecuteAsync        opens the database transaction
  └─ next()                               handler runs, stages aggregates in the change tracker
     ├─ on business success:
     │    IDomainEventsDispatcher.DispatchEventsAsync   drains events, stages outbox rows
     │    IUnitOfWork.CommitAsync                       ONE SaveChangesAsync: aggregates + outbox
     │                                                  ⚠ MUST follow the dispatch, or the outbox
     │                                                     rows it just staged are never written
     └─ on business failure OR exception:
          IUnitOfWork.DiscardChanges                    abandon the staged set (ADR-005)
ITransactionalContext                     commits the transaction, or rolls back on exception
```

**Why the discard is not optional.** `DbContext` is scoped, not per-command. Without it, entities
staged by a failed command stay in the tracker and are written by the next successful command in the
same scope — a command that failed its business rule persisting its data anyway, silently.

**Dependency direction:**
```
MicroKit.MediatR.Behaviors  →  MicroKit.Persistence.Abstractions
                               (ITransactionalContext, IUnitOfWork)
```
Abstractions only — the behavior must never reference `MicroKit.Persistence.EntityFrameworkCore`
(ADR-MEDIATR-011). This is what makes `DiscardChanges()` a contract member rather than an EF call.
