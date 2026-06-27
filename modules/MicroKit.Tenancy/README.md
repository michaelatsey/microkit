# MicroKit.Tenancy

Host-agnostic multitenancy for .NET 10+ distributed architectures.

> **Key differentiator:** Tenant context propagates via `AsyncLocal` — not `IHttpContextAccessor`.
> Works in HTTP requests, background jobs, message consumers, gRPC, and WebSockets without modification.

## Packages

| Package | Description |
|---------|-------------|
| `MicroKit.Tenancy.Abstractions` | Contracts: ITenantContext, ITenantStore, TenantId, ITenantEntity |
| `MicroKit.Tenancy` | Core: AsyncLocal context, resolution pipeline, DI |
| `MicroKit.Tenancy.AspNetCore` | HTTP middleware + resolution strategies |
| `MicroKit.Tenancy.EntityFrameworkCore` | EF Core query filters + SaveChanges interceptor |
| `MicroKit.Tenancy.Analyzers` | Roslyn analyzers: MKT001, MKT002, MKT003 |

## Quick Start

```csharp
// Program.cs
builder.Services.AddMicroKitMultitenancy(options =>
{
    options.AddAspNetCoreResolution(); // header, route, subdomain, claims, host strategies
    options.AddEntityFrameworkCoreIsolation();
});

// Middleware (must be before auth and routing)
app.UseMultitenancy();
```

```csharp
// Inject anywhere — resolves from AsyncLocal
public sealed class OrderService(ITenantContext tenant)
{
    public async ValueTask<Result<Order>> CreateAsync(CreateOrderRequest req, CancellationToken ct)
    {
        var currentTenant = tenant.CurrentTenant;
        if (currentTenant is null)
            return Result<Order>.Failure(MultitenancyErrors.TenantNotFound);
        // ...
    }
}
```

```csharp
// EF Core entity — implements ITenantEntity
public sealed class Order : AggregateRoot<OrderId>, ITenantEntity
{
    public TenantId TenantId { get; private set; } = default!; // stamped by interceptor
    // ...
}
```

## Resolution Strategies (HTTP)

| Strategy | Order | Source |
|----------|-------|--------|
| `HeaderTenantResolutionStrategy` | 1 | `X-Tenant-Id` header |
| `RouteDataTenantResolutionStrategy` | 2 | `{tenantId}` route param |
| `SubdomainTenantResolutionStrategy` | 3 | `{tenant}.app.example.com` |
| `ClaimsTenantResolutionStrategy` | 4 | `tenant_id` JWT claim |
| `HostTenantResolutionStrategy` | 5 | Full host name mapping |

## EF Core Isolation (Shared Mode)

All `ITenantEntity` types automatically receive a `HasQueryFilter` scoped to the current tenant.
The `TenantStampInterceptor` stamps `TenantId` on every `Added` entity during `SaveChanges`.

```csharp
// Cross-tenant admin query — requires explicit justification
var report = await _context.Orders
    // [MTK-BYPASS] Admin: billing aggregation across all tenants
    .IgnoreQueryFilters()
    .GroupBy(o => o.TenantId)
    .ToListAsync(ct);
```

## Analyzers

| ID | Severity | Rule |
|----|----------|------|
| MKT001 | Error | `ITenantEntity` without non-nullable `TenantId` property |
| MKT002 | Warning | `IgnoreQueryFilters()` without `// [MTK-BYPASS]` justification comment |
| MKT003 | Error | `ITenantContextAccessor` injected in a Singleton service |

## License

MIT — see [LICENSE](../../LICENSE).
