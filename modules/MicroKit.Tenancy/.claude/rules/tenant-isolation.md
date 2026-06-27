# Rule: Tenant Isolation — MicroKit.Tenancy

## Always active for any file in the EntityFrameworkCore project.

## Core invariants

### ITenantEntity — the tenant boundary marker
```csharp
// ✅ Every entity requiring tenant isolation implements this
public interface ITenantEntity
{
    TenantId TenantId { get; }
}

// ✅ Application entity
public sealed class Order : IAggregateRoot, ITenantEntity
{
    public TenantId TenantId { get; private set; } = default!;
    // ...
}

// ❌ TenantId nullable — MKT001 analyzer error
public sealed class Invoice : ITenantEntity
{
    public TenantId? TenantId { get; set; } // ❌ must not be nullable
}
```

### EF Core query filter — mandatory for all ITenantEntity
```csharp
// ✅ Every ITenantEntity registered in a multi-tenant DbContext MUST have a filter
protected override void OnModelCreating(ModelBuilder builder)
{
    // Apply to all ITenantEntity types automatically
    foreach (var entityType in builder.Model.GetEntityTypes()
        .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)))
    {
        var parameter = Expression.Parameter(entityType.ClrType, "e");
        var tenantIdProperty = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var currentTenantId = Expression.Constant(_accessor.GetTenant()!.Id);
        var filter = Expression.Lambda(Expression.Equal(tenantIdProperty, currentTenantId), parameter);
        builder.Entity(entityType.ClrType).HasQueryFilter(filter);
    }
}

// ❌ Manual per-entity filter that can be forgotten
builder.Entity<Order>().HasQueryFilter(o => o.TenantId == tenantId); // ❌ manual — misses new entities
```

### SaveChanges interceptor — mandatory stamp
```csharp
// ✅ Stamps TenantId on every ITenantEntity being Added
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

    private void StampTenantId(DbContext context)
    {
        var tenantId = accessor.GetTenant()?.Id
            ?? throw new InvalidOperationException(
                "Cannot save changes without an active tenant context.");

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>()
            .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = tenantId;
        }
    }
}
```

### IgnoreQueryFilters() — bypass requires justification
```csharp
// ✅ Cross-tenant admin query — explicit justification required
var allOrders = await _context.Orders
    // [MTK-BYPASS] Admin report: aggregating across all tenants for billing summary
    .IgnoreQueryFilters()
    .Where(o => o.CreatedAt >= from)
    .ToListAsync(ct);

// ❌ Bypass without comment — MKT002 analyzer warning
var allOrders = await _context.Orders.IgnoreQueryFilters().ToListAsync(ct); // ❌
```

## Rules (non-negotiable)

```
🔴 ITenantEntity.TenantId MUST NOT be nullable
🔴 Every ITenantEntity in a multi-tenant DbContext MUST have a HasQueryFilter
🔴 SaveChanges interceptor MUST be registered for any DbContext with ITenantEntity
🔴 IgnoreQueryFilters() WITHOUT // [MTK-BYPASS] comment → MKT002 warning
🔴 TenantId must not be settable by callers after creation (private set or init)
🟡 Soft-delete entities MUST include TenantId in their composite unique constraints
```

## Isolation modes

```
Shared     → all tenants share tables, isolated by TenantId column (query filter)
Schema     → each tenant has a dedicated schema (TenantId = schema name)
Database   → each tenant has a dedicated database (connection string from ITenantInfo)
```

The EF Core integration supports Shared mode in Phase 1.
Schema and Database modes are planned for Phase 2 (separate DbContext factory per tenant).
