# Standard: Transaction Contracts

**Canonical design for the transaction layer in MicroKit.Persistence.**

---

## Contract Hierarchy

```
IUnitOfWork                    ← commit boundary (Abstractions)
ITransactionalContext          ← explicit transaction management (Abstractions)
ITransactionalUnitOfWork       ← composite for EF Core (EntityFrameworkCore — NOT Abstractions)
  : IUnitOfWork, ITransactionalContext
```

---

## IUnitOfWork — Default Pattern

For the vast majority of command handlers — single aggregate, single commit:

```csharp
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all pending changes accumulated since the last commit or since the
    /// beginning of the current ambient transaction.
    /// </summary>
    ValueTask CommitAsync(CancellationToken ct = default);
}
```

**Invariants:**
- One `CommitAsync()` per command handler invocation
- Called AFTER all domain logic and staging operations (`AddAsync`, `UpdateAsync`, etc.)
- Translates to a single `SaveChangesAsync()` under EF Core
- Throws `PersistenceException` on provider failure (concurrency, constraint violation)

---

## ITransactionalContext — Explicit Transaction

For cross-aggregate commands where all-or-nothing semantics are required:

```csharp
public interface ITransactionalContext : IAsyncDisposable
{
    /// <summary>Begins an explicit database transaction.</summary>
    ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Commits the current explicit transaction.</summary>
    ValueTask CommitTransactionAsync(CancellationToken ct = default);

    /// <summary>Rolls back the current explicit transaction.</summary>
    ValueTask RollbackTransactionAsync(CancellationToken ct = default);
}
```

**Usage:** wrapped in a `try/catch/finally` or the `TransactionBehavior` in MediatR.Behaviors.
`IAsyncDisposable` ensures cleanup if the handler throws before `CommitTransactionAsync`.

---

## ITransactionalUnitOfWork — EF Core Composite

NOT in Abstractions — lives in `MicroKit.Persistence.EntityFrameworkCore`:

```csharp
public interface ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext { }
```

**Why composite:** `EfUnitOfWork` wraps a `DbContext` and needs to call both `SaveChangesAsync`
(commit) and `Database.BeginTransactionAsync` (begin). The composite interface allows consumers
to inject either side independently, but a single `EfUnitOfWork` instance handles both.

**DI registration (one scoped instance, three interface pointers):**
```csharp
services.AddScoped<EfUnitOfWork>();
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork>());
services.AddScoped<ITransactionalContext>(sp => sp.GetRequiredService<EfUnitOfWork>());
services.AddScoped<ITransactionalUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork>());
```

---

## ITransaction

```csharp
public interface ITransaction : IAsyncDisposable
{
    Guid TransactionId { get; }
}
```

Wraps the underlying `IDbContextTransaction` (EF Core) or provider-level transaction.
`IAsyncDisposable` handles rollback if not explicitly committed.

---

## TransactionBehavior Integration

`TransactionBehavior` in `MicroKit.MediatR.Behaviors` depends on `ITransactionalContext`
from this module. The behavior automatically wraps every `ICommand` handler in a DB transaction.
See `.claude-context/context/transaction-behavior-integration.md`.

**Dependency direction:**
```
MicroKit.MediatR.Behaviors  →  MicroKit.Persistence.Abstractions (ITransactionalContext)
```
This is the intended cross-module dependency (MediatR depends on Persistence.Abstractions).
