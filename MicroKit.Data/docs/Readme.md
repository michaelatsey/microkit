# MicroKit.Data

Data access abstractions and EF Core implementations for .NET 10. `IRepository<T>`, `IReadRepository<T>`, and `IUnitOfWork` as the persistence boundary — zero third-party dependencies in the Abstractions package.

---

## What makes this production-grade

**Read/write split enforced by the type system.** `IRepository<T>` extends `IReadRepository<T>`. Query handlers receive `IReadRepository<T>`; command handlers receive the full `IRepository<T>`. The compiler prevents a query handler from calling `AddAsync` or `Update`, and prevents accidental write-path coupling in read models.

**Synchronous staging, asynchronous persistence.** `Update(T entity)` and `Remove(T entity)` are void — they stage changes in the ORM ChangeTracker. `AddAsync` and `AddRangeAsync` are async because some ORMs generate keys on insert. `SaveChangesAsync` on `IUnitOfWork` is the single explicit I/O boundary. This makes transaction scope explicit and testable.

**Composite key support without losing type safety.** `FindByIdAsync` accepts `object id`. This allows composite keys (`FindByIdAsync(new { TenantId = t, Id = id })`) and value-type keys (int, long, Guid) through a single method signature, consistent with EF Core's `FindAsync` behavior.

**Structured error logging in `EfUnitOfWork`.** `EfUnitOfWork<TDbContext>` wraps `SaveChangesAsync` in a try-catch. It logs `Debug` for normal persistence and `Error` for exceptions — with the full exception object, not just the message. Exceptions are re-thrown unchanged; no stack trace is swallowed.

**`ITransactionalContext` for explicit transaction management.** When a multi-step operation must span multiple units of work with a rollback boundary, `ITransactionalContext` exposes `BeginTransaction()`, `CommitAsync()`, and `RollbackAsync()`. The standard `IUnitOfWork` path does not need to know about transactions.

**JSON columns without boilerplate via `MicroKit.EntityFrameworkCore`.** `JsonValueConverters.Create<T>()` returns an EF Core `ValueConverter<T, string>` that serializes to JSON on write and throws `InvalidOperationException` on null deserialization rather than returning null silently. `CreateNullable<T>()` allows null round-trips.

---

## Installation

```shell
# Abstractions — IRepository<T>, IReadRepository<T>, IUnitOfWork, ITransactionalContext
dotnet add package MicroKit.Data.Abstractions

# EF Core implementation of IUnitOfWork
dotnet add package MicroKit.Data.EntityFrameworkCore

# JSON value converters and model builder extensions
dotnet add package MicroKit.EntityFrameworkCore
```

---

## Usage

### Implement a repository

```csharp
using MicroKit.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class OrderRepository : IRepository<Order>
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db) => _db = db;

    public Task<Order?> FindByIdAsync(object id, CancellationToken ct) =>
        _db.Orders.FindAsync([id], ct).AsTask();

    public async Task<IReadOnlyCollection<Order>> GetAllAsync(CancellationToken ct) =>
        await _db.Orders.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyCollection<Order>> FindAsync(
        Expression<Func<Order, bool>> predicate, CancellationToken ct) =>
        await _db.Orders.AsNoTracking().Where(predicate).ToListAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<Order, bool>> predicate, CancellationToken ct) =>
        _db.Orders.AnyAsync(predicate, ct);

    public Task AddAsync(Order entity, CancellationToken ct) =>
        _db.Orders.AddAsync(entity, ct).AsTask();

    public Task AddRangeAsync(IEnumerable<Order> entities, CancellationToken ct) =>
        _db.Orders.AddRangeAsync(entities, ct);

    public void Update(Order entity) => _db.Orders.Update(entity);    // synchronous staging
    public void Remove(Order entity) => _db.Orders.Remove(entity);    // synchronous staging
    public void RemoveRange(IEnumerable<Order> entities) => _db.Orders.RemoveRange(entities);
}
```

### Use in a command handler

```csharp
using MicroKit.Data.Abstractions;

public sealed class CancelOrderHandler : ICommandHandler<CancelOrderCommand, Result>
{
    private readonly IRepository<Order> _orders;
    private readonly IUnitOfWork _uow;

    public CancelOrderHandler(IRepository<Order> orders, IUnitOfWork uow) =>
        (_orders, _uow) = (orders, uow);

    public async Task<Result> HandleAsync(CancelOrderCommand cmd, CancellationToken ct)
    {
        var order = await _orders.FindByIdAsync(cmd.OrderId, ct);
        if (order is null) return Result.Failure(OrderErrors.NotFound);

        order.Cancel();
        _orders.Update(order);           // staged
        await _uow.SaveChangesAsync(ct); // single I/O boundary — logs debug/error via EfUnitOfWork
        return Result.Success();
    }
}
```

### Query handler — read-only repository

```csharp
public sealed class GetActiveOrdersHandler : IQueryHandler<GetActiveOrdersQuery, IReadOnlyList<OrderSummaryDto>>
{
    // IReadRepository<Order> — cannot accidentally call Update, Remove, or AddAsync
    private readonly IReadRepository<Order> _orders;

    public GetActiveOrdersHandler(IReadRepository<Order> orders) => _orders = orders;

    public async Task<IReadOnlyList<OrderSummaryDto>> HandleAsync(
        GetActiveOrdersQuery query, CancellationToken ct)
    {
        var orders = await _orders.FindAsync(o => o.Status == OrderStatus.Pending, ct);
        return orders.Select(o => new OrderSummaryDto(o)).ToList();
    }
}
```

### JSON columns (MicroKit.EntityFrameworkCore)

```csharp
using MicroKit.EntityFrameworkCore.Extensions.Conversions;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>(entity =>
    {
        // Serialize IReadOnlyList<OrderLine> as a JSON column.
        // Throws InvalidOperationException if the stored JSON deserializes to null.
        entity.Property(o => o.Lines)
            .HasConversion(JsonValueConverters.Create<IReadOnlyList<OrderLine>>());

        // Nullable variant — allows the column to contain null JSON
        entity.Property(o => o.Metadata)
            .HasConversion(JsonValueConverters.CreateNullable<Dictionary<string, string>>());
    });
}
```

Both converters use `System.Text.Json`. A custom `JsonSerializerOptions` can be passed as an optional argument to either factory method.

### Explicit transaction management

```csharp
using MicroKit.Data.Abstractions;

// ITransactionalContext wraps IUnitOfWork with a transaction envelope
public sealed class TransferService
{
    private readonly ITransactionalContext _tx;
    private readonly IRepository<Account> _accounts;

    public TransferService(ITransactionalContext tx, IRepository<Account> accounts) =>
        (_tx, _accounts) = (tx, accounts);

    public async Task TransferAsync(Guid fromId, Guid toId, Money amount, CancellationToken ct)
    {
        using var tx = _tx.BeginTransaction();
        try
        {
            var from = await _accounts.FindByIdAsync(fromId, ct);
            var to   = await _accounts.FindByIdAsync(toId, ct);

            from!.Debit(amount);
            to!.Credit(amount);

            _accounts.Update(from);
            _accounts.Update(to);
            await _tx.CommitAsync(ct);
        }
        catch
        {
            await _tx.RollbackAsync(ct);
            throw;
        }
    }
}
```

---

## Configuration

```csharp
using MicroKit.Data.Abstractions;
using MicroKit.Data.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// EF Core IUnitOfWork — logs Debug on success, Error on failure, re-throws unchanged
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork<AppDbContext>>();

// Register repositories — both read and write interfaces pointing to the same implementation
builder.Services.AddScoped<IRepository<Order>, OrderRepository>();
builder.Services.AddScoped<IReadRepository<Order>, OrderRepository>();
```

---

## Package dependency graph

```
MicroKit.Data.Abstractions
    (no NuGet dependencies)

MicroKit.Data.EntityFrameworkCore
    MicroKit.Data.Abstractions
    Microsoft.EntityFrameworkCore

MicroKit.EntityFrameworkCore
    Microsoft.EntityFrameworkCore
```
