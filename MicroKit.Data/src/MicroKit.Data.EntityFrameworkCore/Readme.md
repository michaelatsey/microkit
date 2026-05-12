# MicroKit.Data.EntityFrameworkCore

EF Core implementation of `IUnitOfWork`. `EfUnitOfWork<TDbContext>` delegates to `TDbContext.SaveChangesAsync` with structured error logging. Register it alongside your EF Core `DbContext` to satisfy the `IUnitOfWork` contract in application services.

## When to use

Use this when your persistence layer is EF Core and you want `IUnitOfWork` wired to an existing `DbContext`. For JSON column mapping on EF Core entities, use `MicroKit.EntityFrameworkCore` instead (separate module, no overlap).

## Installation

```
dotnet add package MicroKit.Data.EntityFrameworkCore
```

## Key types

| Type | Description |
|---|---|
| `EfUnitOfWork<TDbContext>` | Implements `IUnitOfWork`; calls `DbContext.SaveChangesAsync` and logs errors via `ILogger` before re-throwing |

## Usage

```csharp
// Registration
services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(connectionString));

services.AddScoped<IUnitOfWork, EfUnitOfWork<AppDbContext>>();

// In a command handler
public class CreateOrderHandler(
    IRepository<Order> orders,
    IUnitOfWork unitOfWork)
    : CommandHandler<CreateOrderCommand>
{
    public override async Task HandleAsync(CreateOrderCommand command, CancellationToken ct)
    {
        var order = new Order(command.CustomerId);
        await orders.AddAsync(order, ct);
        await unitOfWork.SaveChangesAsync(ct); // commits via EF Core
    }
}
```

`EfUnitOfWork<TDbContext>` logs at `Debug` level before persisting and at `Error` level when an exception occurs, then re-throws the original exception unchanged.

## Dependencies

- `MicroKit.Data.Abstractions`
- `Microsoft.EntityFrameworkCore`
