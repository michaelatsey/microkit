# Command: /new-tenant-store

Scaffold a custom `ITenantStore` implementation.

## Usage
```
/new-tenant-store [store-name] [backing-store]
```

## Examples
```
/new-tenant-store Database ef-core
/new-tenant-store Redis cache
```

## What gets generated

1. `src/MicroKit.Multitenancy/Stores/{Name}TenantStore.cs`
   - `sealed class` implementing `ITenantStore`
   - `FindAsync(TenantId, CancellationToken) → ValueTask<Result<ITenantInfo>>`
   - `ListAllAsync(CancellationToken) → ValueTask<IReadOnlyList<ITenantInfo>>`
   - XML documentation

2. Test: `tests/MicroKit.Multitenancy.UnitTests/Stores/{Name}TenantStoreTests.cs`
   - FindAsync returns tenant when found
   - FindAsync returns failure when not found
   - ListAllAsync returns all tenants

## Template

```csharp
/// <summary>
/// Tenant store backed by [backing store].
/// </summary>
public sealed class {Name}TenantStore : ITenantStore
{
    /// <inheritdoc />
    public async ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default)
    {
        // TODO: lookup implementation
        // Return Result<ITenantInfo>.Failure(MultitenancyErrors.TenantNotFound) if not found
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default)
    {
        // TODO: list implementation
    }
}
```
