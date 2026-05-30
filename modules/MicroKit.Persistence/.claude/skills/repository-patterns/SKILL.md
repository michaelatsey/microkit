---
name: repository-patterns
description: How to design and implement repositories in MicroKit.Persistence — IRepository vs IReadRepository split, aggregate constraints, custom finder methods, InMemoryRepository for testing. Use when modeling new aggregates, deciding between write/read repositories, or implementing the EF Core versions.
---

# Skill: Repository Patterns

How to design and implement repositories correctly in MicroKit.Persistence.

## The Write/Read Split

Every aggregate gets two repository contracts:

```csharp
// WRITE — command handlers use this
public interface IOrderRepository : IRepository<Order>
{
    ValueTask<Order?> FindByNumberAsync(OrderNumber number, CancellationToken ct = default);
}

// READ — query handlers use this (no mutations)
public interface IOrderReadRepository : IReadRepository<Order>
{
    ValueTask<OrderSummaryDto?> GetSummaryAsync(OrderId id, CancellationToken ct = default);
    ValueTask<IPagedResult<OrderDto>> GetPagedAsync(OrderFilterSpec spec, PaginationOptions p, CancellationToken ct = default);
}
```

## Aggregate Root Constraint

Only `IAggregateRoot` types get their own repository.

```csharp
// ✅ Order is an aggregate root
public interface IOrderRepository : IRepository<Order>  // Order : IAggregateRoot ✅

// ❌ OrderLine is a child entity — persisted via Order
public interface IOrderLineRepository : IRepository<OrderLine> // ❌ not an aggregate root
```

## CommitAsync Pattern (write handler)

```csharp
public async ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct)
{
    var order = Order.Create(cmd.UserId, cmd.Items); // domain logic
    await _repo.AddAsync(order, ct);                 // stage
    await _uow.CommitAsync(ct);                      // single commit
    await _events.PublishAsync(new OrderCreatedEvent(order.Id), ct);
    return Result.Success(order.Id);
}
```

## Read Pattern (query handler)

```csharp
public async ValueTask<Result<IPagedResult<OrderDto>>> Handle(GetOrdersQuery q, CancellationToken ct)
{
    var opts = new QueryOptions<Order>(new OrdersByUserSpec(q.UserId))
        .WithPagination(q.Page, q.PageSize);

    var orders = await _readRepo.ListAsync(opts, ct).ConfigureAwait(false);
    return Result.Success(PagedResult.From(orders, q.Page, q.PageSize));
}
```

## InMemoryRepository (for testing)

```csharp
var repo = new InMemoryRepository<Order>();
var uow = new InMemoryUnitOfWork();

var order = Order.Create(userId, items);
await repo.AddAsync(order);
await uow.CommitAsync();

var found = await repo.FindAsync(order.Id);
found.ShouldNotBeNull();
```

## EF Core Implementation Pattern

```csharp
public sealed class EfOrderRepository(AppDbContext ctx, IUnitOfWork uow)
    : IOrderRepository
{
    public async ValueTask<Order?> FindAsync(OrderId id, CancellationToken ct)
        => await ctx.Orders.FindAsync([id.Value], ct).ConfigureAwait(false);

    public async ValueTask AddAsync(Order order, CancellationToken ct)
        => await ctx.Orders.AddAsync(order, ct).ConfigureAwait(false);

    public async ValueTask UpdateAsync(Order order, CancellationToken ct)
    {
        ctx.Orders.Update(order);
        await ValueTask.CompletedTask;
    }

    public async ValueTask DeleteAsync(Order order, CancellationToken ct)
    {
        ctx.Orders.Remove(order);
        await ValueTask.CompletedTask;
    }

    public async ValueTask CommitAsync(CancellationToken ct)
        => await uow.CommitAsync(ct).ConfigureAwait(false);
}
```
