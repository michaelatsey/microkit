---
name: ef-core-patterns
description: EF Core patterns used in MicroKit.Persistence — entity configuration, value converters, AsNoTracking enforcement, QueryOptions application, split queries, compiled queries, migrations. Use when implementing or reviewing EF Core repositories, configurations, or migrations.
---

# Skill: EF Core Patterns

How to implement EF Core correctly in MicroKit.Persistence.

## Entity Configuration

Always use `IEntityTypeConfiguration<T>` — never `OnModelCreating` sprawl:

```csharp
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
               .HasConversion(id => id.Value, v => new OrderId(v));
        builder.OwnsOne(o => o.TotalAmount, ab =>
            ab.Property(a => a.Value).HasColumnName("TotalAmount").HasPrecision(18, 2));
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
    }
}
```

## Read Path (AsNoTracking mandatory)

```csharp
public async ValueTask<IReadOnlyList<Order>> ListAsync(QueryOptions<Order> opts, CancellationToken ct)
{
    var query = _ctx.Orders
        .AsNoTracking()                     // ← first line, always
        .Where(opts.Specification.Criteria);

    if (opts.Includes is not null)
        query = opts.Includes(query);

    if (opts.Pagination is { } p)
        query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);

    return await query.ToListAsync(ct).ConfigureAwait(false);
}
```

## Write Path (FindAsync + CommitAsync)

```csharp
public async ValueTask<Order?> FindAsync(OrderId id, CancellationToken ct)
    => await _ctx.Orders.FindAsync([id.Value], ct).ConfigureAwait(false);

// Handler: single CommitAsync at the end
var order = await _repo.FindAsync(cmd.OrderId, ct);
order!.AddLine(cmd.ProductId, cmd.Quantity);
await _uow.CommitAsync(ct);
```

## Split Query for Multi-Collection Includes

```csharp
// ✅ Avoids Cartesian explosion
var opts = new QueryOptions<Order>(spec)
    .WithIncludes(q => q.Include(o => o.Lines).ThenInclude(l => l.Product)
                        .Include(o => o.Events))
    .AsSplitQuery();
```

## Compiled Queries (hot paths)

```csharp
private static readonly Func<AppDbContext, Guid, CancellationToken, Task<Order?>> _findByIdQuery =
    EF.CompileAsyncQuery((AppDbContext ctx, Guid id, CancellationToken ct) =>
        ctx.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id));
```

## Migrations

```bash
# Add migration
dotnet ef migrations add {MigrationName} \
  --project src/MicroKit.Persistence.EntityFrameworkCore/

# Generate idempotent script (for DBA review)
dotnet ef migrations script --idempotent -o migrations/{name}.sql

# Apply
dotnet ef database update
```

## Provider-Specific Configuration

```csharp
// PostgreSQL — snake_case and jsonb
builder.Property(u => u.Metadata).HasColumnType("jsonb");

// SQL Server — temporal tables
builder.ToTable("Users", t => t.IsTemporal());
```
