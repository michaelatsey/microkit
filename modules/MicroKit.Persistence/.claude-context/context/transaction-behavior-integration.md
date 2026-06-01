# Context: Transaction Behavior Integration

**How MicroKit.MediatR.Behaviors.TransactionBehavior integrates with MicroKit.Persistence.Abstractions.**

---

## Overview

`TransactionBehavior` in `MicroKit.MediatR.Behaviors` provides automatic ambient DB transaction
management for command handlers. It depends on `ITransactionalContext` from
`MicroKit.Persistence.Abstractions`.

This is the **only cross-module dependency** between MediatR and Persistence at the contract level.

---

## Dependency Direction

```
MicroKit.MediatR.Behaviors
    └── PackageReference → MicroKit.Persistence.Abstractions
                               └── provides ITransactionalContext
```

**Why this direction is correct:**
- `MicroKit.MediatR.Behaviors` is Level 2 — it may depend on another Level 2 Abstractions package
- `MicroKit.Persistence.Abstractions` is the published contract — it does not depend on MediatR
- The direction is one-way: Persistence does not know about MediatR

---

## TransactionBehavior Design

```csharp
// In MicroKit.MediatR.Behaviors
public sealed class TransactionBehavior<TRequest, TResponse>(ITransactionalContext tx)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Commands only — queries never participate in a write transaction
    public override int Order => PipelineOrder.Transaction; // e.g., 50 (before Validation at 300)

    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // Pass through for non-commands (queries, events)
        if (request is not ICommand and not ICommand<TResponse>)
            return await next().ConfigureAwait(false);

        // Wrap in DB transaction
        await using var transaction = await tx.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var response = await next().ConfigureAwait(false);

            // Commit only on success; skip commit if Result.IsFailure
            if (response is not Result { IsFailure: true })
                await tx.CommitTransactionAsync(ct).ConfigureAwait(false);

            return response;
        }
        catch
        {
            await tx.RollbackTransactionAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
```

---

## Registration (Consumer Application)

```csharp
// Both modules registered, TransactionBehavior opt-in
services.AddMicroKitMediatR(cfg =>
    cfg.AddTransactionBehavior());              // activates TransactionBehavior

services.AddMicroKitPersistence(persistence =>
    persistence.AddEntityFrameworkCore(ef =>
        ef.UsePostgreSQL(connectionString)));

// Under the hood, TransactionBehavior injects ITransactionalContext
// which is satisfied by EfUnitOfWork (registered via AddEntityFrameworkCore)
```

---

## Interaction Flow (per command dispatch)

```
Request arrives at pipeline
    → TransactionBehavior (order 50) — BeginTransactionAsync
        → LoggingBehavior (100)
        → AuthorizationBehavior (200)
        → ValidationBehavior (300)
        → [optional: IdempotencyBehavior (400)]
        → Handler — executes domain logic, calls CommitAsync via IUnitOfWork

    ← on success: TransactionBehavior CommitTransactionAsync
    ← on failure or throw: TransactionBehavior RollbackTransactionAsync
```

The command handler's `IUnitOfWork.CommitAsync()` call inside the handler IS INSIDE the open
transaction. It triggers `SaveChangesAsync` on the `DbContext`. The `TransactionBehavior` then
commits the ambient `IDbContextTransaction`.

---

## IUnitOfWork vs ITransactionalContext Distinction

| Interface | Who injects | What it does |
|---|---|---|
| `IUnitOfWork` | Command handler | Stage+commit the current aggregate changes (`SaveChangesAsync`) |
| `ITransactionalContext` | `TransactionBehavior` | Begin/commit/rollback a DB-level transaction |

The handler doesn't need to know about the transaction — `IUnitOfWork.CommitAsync()` inside
an active `IDbContextTransaction` is automatically part of that transaction.

---

## When TransactionBehavior Is NOT Used

- Query handlers — reads do not require write transactions
- Commands that are intentionally non-transactional (idempotent retry scenarios)
- Event handlers (domain event dispatch is post-transaction)

For those cases, commands simply call `IUnitOfWork.CommitAsync()` directly without an ambient
transaction. If a failure occurs mid-handler (before `CommitAsync`), EF's change tracker is
discarded at the end of the scoped lifetime.

---

## PipelineOrder Reservation

`PipelineOrder.Transaction` = **50** — before `Logging (100)` is NOT correct; it should be
between Logging and Authorization:

```
50   TransactionBehavior — begins DB transaction
100  LoggingBehavior    — observes everything (including transaction scope)
200  AuthorizationBehavior
300  ValidationBehavior
...
```

Placing Transaction at 50 ensures the entire pipeline (Logging included) executes within a
database transaction, allowing the LoggingBehavior to see the transaction state.
