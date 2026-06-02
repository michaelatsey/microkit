# Rule: Architecture — MicroKit.Multitenancy

## Always active for every file in this module.

## Tenant Context Pattern

### Host-agnostic design (the key differentiator)
```csharp
// ✅ Core is NOT bound to IHttpContextAccessor
// ITenantContextAccessor is backed by AsyncLocal — works in HTTP, queues, jobs, gRPC

// ❌ Binding tenant context to HTTP pipeline
public sealed class HttpTenantContextAccessor(IHttpContextAccessor http) : ITenantContextAccessor
{
    public ITenantInfo? GetTenant() => http.HttpContext?.Items["Tenant"] as ITenantInfo; // ❌
}
// This belongs in AspNetCore bridge, not Core
```

### AsyncLocal-backed accessor (Core)
```csharp
// ✅ Correct — AsyncLocal, no HTTP dependency
public sealed class AsyncLocalTenantContextAccessor : ITenantContextAccessor
{
    private readonly AsyncLocal<ITenantInfo?> _current = new();

    public ITenantInfo? GetTenant() => _current.Value;

    public void SetTenant(ITenantInfo? tenant) => _current.Value = tenant;

    public IDisposable CreateScope(ITenantInfo tenant)
    {
        var previous = _current.Value;
        _current.Value = tenant;
        return new TenantScope(_current, previous);
    }
}
```

## Resolution Pipeline Pattern

```
Strategy 1 (Order=1) → TryResolveAsync → Result<TenantId>.Failure
Strategy 2 (Order=2) → TryResolveAsync → Result<TenantId>.Success(id)  ← short-circuit
                                              ↓
                             ITenantStore.FindAsync(id)
                                              ↓
                             ITenantResolver returns Result<ITenantInfo>.Success(info)
```

## EF Core Isolation (EntityFrameworkCore project)

```
ITenantDbContext → exposes current TenantId to query filter registration
TenantStampInterceptor → stamps TenantId on Added entities via SaveChangesInterceptor
HasQueryFilter (automatic) → applied to ALL ITenantEntity types via OnModelCreating
IgnoreTenantScope → IDisposable scope for admin/cross-tenant queries
```

## Layer responsibilities

| Package | Responsibility |
|---------|---------------|
| `Abstractions` | Contracts only — ITenantContext, ITenantStore, TenantId VO, ITenantEntity marker |
| `Core` | AsyncLocal accessor, resolution pipeline orchestrator, in-memory/config store, DI |
| `AspNetCore` | HTTP middleware, HTTP-specific resolution strategies (header, route, subdomain, claim, host) |
| `EntityFrameworkCore` | Query filter, SaveChanges interceptor, IDisposable bypass scope, DI registration |
| `Analyzers` | Build-time enforcement (MKT001, MKT002, MKT003) |

## Strict rules

```
🔴 ITenantContextAccessor never in a Singleton — MKT003 blocks this
🔴 ITenantResolutionStrategy never throws — always Result<T>
🔴 TenantId never nullable on ITenantEntity — MKT001 blocks this
🔴 IgnoreQueryFilters() never without // [MTK-BYPASS] — MKT002 warns
🔴 EF Core types never in Abstractions or Core
🔴 IHttpContextAccessor never in Abstractions or Core
🟡 Phase 1 EF isolation: Shared mode only (row-level, TenantId column)
🟡 Schema/Database modes are Phase 2 — do not add complexity now
```
