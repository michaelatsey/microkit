---
name: transaction-patterns
description: How transactions work in MicroKit.Persistence — IUnitOfWork, ITransactionalContext, ITransactionalUnitOfWork (EF Core composite), and the MediatR TransactionBehavior integration. Use when implementing transactional commands, cross-aggregate atomicity, or the MediatR pipeline integration.
---

# Skill: Transaction Patterns

How the transaction model works in MicroKit.Persistence.

## The Three Contracts

```
IUnitOfWork         → CommitAsync() — one commit per command (default)
ITransactionalContext → BeginTransactionAsync, CommitTransactionAsync, RollbackTransactionAsync
ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext — EF Core composite (not in Abstractions)
```

## Default Pattern — IUnitOfWork

For most command handlers, inject `IUnitOfWork` and call `CommitAsync()` once:

```csharp
public sealed class CreateOrderHandler(IOrderRepository repo, IUnitOfWork uow)
    : ICommandHandler<CreateOrderCommand, Result<OrderId>>
{
    public async ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.UserId, cmd.Items);
        await repo.AddAsync(order, ct);
        await uow.CommitAsync(ct);                  // single commit
        return Result.Success(order.Id);
    }
}
```

## Ambient Transaction — ITransactionalContext

For cross-aggregate operations that require an explicit DB transaction:

```csharp
public sealed class TransferFundsHandler(
    IAccountRepository repo,
    IUnitOfWork uow,
    ITransactionalContext tx)
    : ICommandHandler<TransferFundsCommand, Result<Unit>>
{
    public async ValueTask<Result<Unit>> Handle(TransferFundsCommand cmd, CancellationToken ct)
    {
        await using var transaction = await tx.BeginTransactionAsync(ct);
        try
        {
            var source = await repo.FindAsync(cmd.SourceAccountId, ct);
            var dest   = await repo.FindAsync(cmd.DestAccountId, ct);

            source!.Debit(cmd.Amount);
            dest!.Credit(cmd.Amount);

            await uow.CommitAsync(ct);
            await tx.CommitTransactionAsync(ct);
            return Result.Success(Unit.Value);
        }
        catch
        {
            await tx.RollbackTransactionAsync(ct);
            throw;
        }
    }
}
```

## MediatR TransactionBehavior Integration

`TransactionBehavior` in `MicroKit.MediatR.Behaviors` injects `ITransactionalContext` from this module.
It wraps every `ICommand` handler in an automatic transaction. See:
`.claude-context/context/transaction-behavior-integration.md`

```csharp
// TransactionBehavior (in MicroKit.MediatR.Behaviors) — simplified
if (request is not ICommand)
    return await next().ConfigureAwait(false);

await using var transaction = await _tx.BeginTransactionAsync(ct).ConfigureAwait(false);
try
{
    var response = await next().ConfigureAwait(false);
    await _tx.CommitTransactionAsync(ct).ConfigureAwait(false);
    return response;
}
catch
{
    await _tx.RollbackTransactionAsync(ct).ConfigureAwait(false);
    throw;
}
```

## EfUnitOfWork (ITransactionalUnitOfWork)

The EF Core implementation lives in `MicroKit.Persistence.EntityFrameworkCore` and implements both:

```csharp
services.AddScoped<EfUnitOfWork>();
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork>());
services.AddScoped<ITransactionalContext>(sp => sp.GetRequiredService<EfUnitOfWork>());
services.AddScoped<ITransactionalUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork>());
```

## When NOT to Use Transactions

- Read-only query handlers — never inject `ITransactionalContext`
- Idempotent commands that tolerate partial execution
- Event-driven handlers that retry independently (outbox pattern is better)
