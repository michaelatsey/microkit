# Standard: EF Core Tenant Isolation

## ITenantEntity marker

```csharp
/// <summary>Marker interface for EF Core entities that are scoped per tenant.</summary>
public interface ITenantEntity
{
    /// <summary>The tenant this entity belongs to. Must not be nullable.</summary>
    TenantId TenantId { get; }
}
```

## Automatic query filter registration

```csharp
// In ITenantDbContext-aware OnModelCreating override
// Applied to every entity type implementing ITenantEntity
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    foreach (var entityType in modelBuilder.Model.GetEntityTypes()
        .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)))
    {
        var tenantId = _accessor.GetTenant()?.Id;
        modelBuilder.Entity(entityType.ClrType)
            .HasQueryFilter(BuildFilter(entityType.ClrType, tenantId));
    }
}
```

## TenantStampInterceptor

```csharp
public sealed class TenantStampInterceptor(ITenantContextAccessor accessor)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampTenantId(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StampTenantId(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    private void StampTenantId(DbContext context)
    {
        var tenantId = accessor.GetTenant()?.Id
            ?? throw new InvalidOperationException(
                "Cannot persist entities without an active tenant context.");

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>()
            .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = tenantId;
        }
    }
}
```

## IgnoreTenantScope — admin bypass

```csharp
// Usage:
using (_ignoreTenantScope.Begin())
{
    // [MTK-BYPASS] Admin: aggregate billing data across all tenants
    var allOrders = await _context.Orders.IgnoreQueryFilters().ToListAsync(ct);
}
```

## DI registration

```csharp
// In EntityFrameworkCore DI extension
services.AddScoped<TenantStampInterceptor>();
services.AddDbContext<TAppDbContext>((sp, opts) =>
{
    opts.AddInterceptors(sp.GetRequiredService<TenantStampInterceptor>());
    // ...
});
```
