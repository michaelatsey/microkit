# Workflow: Adding a Repository

How a consuming application adds persistence for an aggregate, and how to review it. The
repositories live in the application, not in this module. Reference:
`samples/MicroKit.Persistence.Samples` (`Repositories.cs`, `Queries.cs`, `Composition.cs`).

## 1. Confirm the aggregate root

Only an `IAggregateRoot` with a strongly-typed `IEntityId` key gets a repository. Child entities
are loaded and saved with their root.

## 2. Write side

Often the generic contract is enough: inject `IRepository<Order, OrderId>` and register
`EfRepository<Order, OrderId, ShopDbContext>`. A typed repository adds finders:

```csharp
public interface IOrderRepository : IRepository<Order, OrderId>
{
    ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default);
}

public sealed class EfOrderRepository(ShopDbContext context)
    : EfRepository<Order, OrderId, ShopDbContext>(context), IOrderRepository
{
    public async ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default)
    {
        var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
        return orders.SingleOrDefault();
    }
}
```

- No `CommitAsync`, no `UpdateAsync`: load, mutate the tracked aggregate, and let `IUnitOfWork`
  commit.
- A finder calls `LoadAggregatesAsync` so it returns the same whole aggregate as `FindAsync`, recorded
  and checked; it states which roots, never what is loaded with them.
- The boundary is the model's: mark reference data `IsOutsideAggregate()` in `OnModelCreating`.
  The context needs `UseMicroKitPersistence()` (the `EfCoreBuilder` registrations call it).

## 3. Read side

Inject `IReadRepository<Order>` and register `EfReadRepository<Order, ShopDbContext>`. For
projections, derive a typed read repository:

```csharp
public interface IOrderReadRepository : IReadRepository<Order>
{
    ValueTask<IReadOnlyList<OrderSummary>> ListSummariesAsync(CancellationToken ct = default);
}

public sealed class EfOrderReadRepository(ShopDbContext context, ISpecificationEvaluator evaluator, PaginationLimits limits)
    : EfReadRepository<Order, ShopDbContext>(context, evaluator, limits), IOrderReadRepository
{
    public async ValueTask<IReadOnlyList<OrderSummary>> ListSummariesAsync(CancellationToken ct = default) =>
        await Context.Orders.AsNoTracking()
            .Select(o => new OrderSummary(o.Id, o.Number))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
```

No mutation (MKP001/MKP002), no `IQueryable<T>` return (MKP005).

## 4. Mapping

An `IEntityTypeConfiguration<Order>`; identifiers, `Actor` and audit instants come from
`AddDomainConventions` in `ConfigureConventions`.

## 5. Registration

```csharp
services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef => ef
    .UsePostgreSQL<ShopDbContext>(connectionString)
    .AddUnitOfWork<ShopDbContext>()));

services.AddScoped<IOrderRepository, EfOrderRepository>();
services.AddScoped<IReadRepository<Order>, EfReadRepository<Order, ShopDbContext>>();
```

## 6. Tests

Handlers: `InMemoryRepository<Order, OrderId>` + `InMemoryUnitOfWork`. EF behaviour (aggregate
load, finders): SQLite integration tests. `test-generator` produces the matrix of
`.claude/rules/testing.md`.

## 7. Checks

```bash
dotnet build modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release
dotnet test  modules/MicroKit.Persistence/MicroKit.Persistence.slnx -c Release
```
