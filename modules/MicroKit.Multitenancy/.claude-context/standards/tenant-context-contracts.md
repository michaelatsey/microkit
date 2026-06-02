# Standard: Tenant Context Contracts

## ITenantInfo — the tenant descriptor

```csharp
public interface ITenantInfo
{
    /// <summary>Unique tenant identifier.</summary>
    TenantId Id { get; }

    /// <summary>Human-readable tenant name.</summary>
    string Name { get; }

    /// <summary>Optional connection string override for database-per-tenant mode.</summary>
    string? ConnectionString { get; }

    /// <summary>Optional schema name override for schema-per-tenant mode.</summary>
    string? SchemaName { get; }

    /// <summary>Whether this tenant is active and allowed to make requests.</summary>
    bool IsActive { get; }
}
```

## ITenantContext — read-only context accessor

```csharp
public interface ITenantContext
{
    /// <summary>
    /// The current tenant, or <see langword="null"/> if no tenant has been resolved.
    /// </summary>
    ITenantInfo? CurrentTenant { get; }
}
```

## ITenantContextAccessor — read/write accessor

```csharp
public interface ITenantContextAccessor : ITenantContext
{
    /// <summary>Sets the current tenant for the active async execution context.</summary>
    void SetTenant(ITenantInfo? tenant);

    /// <summary>
    /// Creates a scoped tenant context. Restores the previous tenant on disposal.
    /// Required for background tasks and parallel work items.
    /// </summary>
    IDisposable CreateScope(ITenantInfo tenant);
}
```

## TenantId — value object

```csharp
/// <summary>Strongly-typed tenant identifier.</summary>
public sealed record TenantId(Guid Value)
{
    /// <summary>Creates a new random TenantId.</summary>
    public static TenantId NewId() => new(Guid.NewGuid());

    /// <summary>Returns the string representation of the underlying Guid.</summary>
    public override string ToString() => Value.ToString();
}
```

## Registration

```csharp
// ITenantContextAccessor MUST be Scoped
services.AddScoped<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();
// ITenantContext resolves the same instance
services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ITenantContextAccessor>());
```
