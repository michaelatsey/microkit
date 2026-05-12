# MicroKit.MultiTenancy.Abstractions

Core multi-tenancy contracts with no external dependencies. Defines the full resolution pipeline — from identifying the tenant to fetching it from a store and caching it for the request — plus the entity marker and auxiliary service interfaces.

## When to use

Reference `MicroKit.MultiTenancy.Abstractions` in:
- Application and domain packages that need to read the current tenant (`ITenantContext`, `ITenant`)
- Domain entities that belong to a specific tenant (`IHasMultiTenant`)
- Infrastructure packages implementing custom tenant resolution strategies or stores

The concrete implementations (`TenantContext`, `Tenant`) are in `MicroKit.MultiTenancy`. Cache and store implementations are in their respective integration packages.

## Installation

```
dotnet add package MicroKit.MultiTenancy.Abstractions
```

## Key types

| Type | Description |
|---|---|
| `ITenant` | `Id`, `Name`, `ConnectionString`, extensible `Items` dictionary |
| `ITenantContext` | Scoped context holding the resolved `ITenant`; `IsResolved`, `EnsureResolved()` |
| `ITenantContextSetter` | Allows middleware to push a resolved tenant into the current scope |
| `ITenantStore` | `GetTenantAsync(identifier)` — fetches from the backing store |
| `ITenantRegistry` | `GetAllTenantsAsync()` — returns all registered tenant identifiers |
| `ITenantResolutionStrategy` | `ResolveAsync()` — extracts a tenant identifier from the ambient request context (header, JWT, subdomain, etc.) |
| `ITenantCache` | Two-level cache abstraction: `GetAsync`, `SetAsync`, `RemoveAsync` |
| `ITenantEndpointProvider` | Resolves per-tenant endpoint URIs for geo-routing |
| `ITenantRegionResolver` | Maps a tenant identifier to its deployment region |
| `ITenantMetadataRepository` | Reads persisted tenant metadata records |
| `IHasMultiTenant` | Marker interface for entities that require tenant-scoped EF Core global query filters |
| `IModuleValidator` | Startup validation hook for multi-tenancy module configuration |

## Usage

```csharp
// Read the current tenant in an application service
public class InvoiceService(ITenantContext tenantContext)
{
    public void GenerateInvoice()
    {
        tenantContext.EnsureResolved();
        var tenantId = tenantContext.Tenant!.Id;
        // ...
    }
}

// Custom resolution strategy (e.g. from X-Tenant-Id header)
public class HeaderTenantResolutionStrategy(IHttpContextAccessor http)
    : ITenantResolutionStrategy
{
    public ValueTask<string?> ResolveAsync(CancellationToken ct)
    {
        var id = http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        return ValueTask.FromResult(id);
    }
}
```

## Dependencies

None.
