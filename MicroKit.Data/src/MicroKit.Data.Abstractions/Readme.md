# MicroKit.Data.Abstractions

Data access contracts with no external dependencies. Defines the generic repository pattern interfaces (`IRepository<T>`, `IReadRepository<T>`), the Unit of Work contract (`IUnitOfWork`), and a transactional execution scope (`ITransactionalContext`). Application layer code depends only on these interfaces.

## When to use

Reference `MicroKit.Data.Abstractions` in application and domain layers where repositories are used. The concrete EF Core implementations live in `MicroKit.Data.EntityFrameworkCore`.

## Installation

```
dotnet add package MicroKit.Data.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `IReadRepository<T>` | `FindByIdAsync`, `GetAllAsync`, `FindAsync(predicate)`, `ExistsAsync(predicate)` |
| `IRepository<T>` | Extends `IReadRepository<T>` with `AddAsync`, `AddRangeAsync`, `Update`, `Remove`, `RemoveRange` |
| `IUnitOfWork` | `SaveChangesAsync(CancellationToken)` — commits staged changes |
| `ITransactionalContext` | `ExecuteAsync(Func<CancellationToken, Task>)` — wraps an operation in a database transaction |

## Usage

```csharp
// Application service depending only on abstractions
public class OrderService(
    IRepository<Order> orders,
    IUnitOfWork unitOfWork)
{
    public async Task<Guid> CreateAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(cmd.CustomerId);
        await orders.AddAsync(order, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return order.Id;
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken ct)
        => orders.FindByIdAsync(id, ct);
}

// Registration (with EF Core implementation)
services.AddScoped<IRepository<Order>, EfRepository<Order, AppDbContext>>();
services.AddScoped<IUnitOfWork, EfUnitOfWork<AppDbContext>>();
```

## Dependencies

None.
