# MicroKit.MultiTenancy

Pluggable multi-tenancy for .NET 10. Per-request immutable `TenantContext`, startup-time wiring validation, region-aware endpoint routing, composite region resolver chains — and zero third-party dependencies in the Abstractions package.

---

## What makes this production-grade

**`SetTenant` is write-once per scope.** `TenantContext.SetTenant` throws `InvalidOperationException` if called a second time on the same scoped instance. The middleware resolves the tenant once at the start of the request and sets it. From that point forward, any code that receives `ITenantContext` gets the same immutable tenant — there is no risk of a handler or background service overwriting the tenant mid-request.

**`EnsureResolved` as a contract.** `ITenantContext.EnsureResolved()` throws if the tenant has not been resolved. Call it at the top of any handler that requires a tenant and get an unambiguous `InvalidOperationException` instead of a null-reference downstream. This is a first-class API, not a defensive null check scattered across handlers.

**Startup-time DI validation.** `MultiTenantValidationService` is an `IHostedService` that runs `IModuleValidator.Validate()` for every registered validator during `StartAsync`. The `MultiTenancyModuleValidator` checks that `ITenantRegistry` is registered. If it is missing, the application fails to start with an `InvalidOperationException` naming the missing service — not a `NullReferenceException` on the first request.

**Composite region resolver.** `CompositeTenantRegionResolver` chains multiple `ITenantRegionResolver` implementations and returns the first non-null result. Resolvers are tried in registration order. This allows a claim-based resolver to take precedence, with a database-backed resolver as fallback.

**Region-aware endpoint routing.** `RegionAwareTenantEndpointProvider` resolves the service endpoint URL for a tenant based on its region — enabling geo-partitioned architectures where each tenant's data lives in a different region and outgoing calls must target the correct regional endpoint.

**`SkipTenantValidationAttribute` for pre-auth endpoints.** Decorate a controller or minimal API group with `SkipTenantValidationAttribute` to exempt it from tenant resolution. This is the correct place to exempt public sign-up, health checks, and OIDC callback endpoints without disabling middleware globally.

**Three built-in resolution strategies.** `HeaderResolutionStrategy` reads a configurable header (default: `X-Tenant-Id`). `JwtClaimResolutionStrategy` reads a configurable JWT claim. Both are registered as both `IHttpTenantResolutionStrategy` and `ITenantResolutionStrategy`, so non-HTTP consumers can resolve the strategy through the base interface.

---

## Installation

```shell
# Contracts — ITenant, ITenantContext, ITenantResolutionStrategy — zero deps
dotnet add package MicroKit.MultiTenancy.Abstractions

# TenantContext, region resolvers, startup validator, AddMicroKitMultiTenancy
dotnet add package MicroKit.MultiTenancy

# HTTP resolution strategies, TenantResolutionMiddleware
dotnet add package MicroKit.MultiTenancy.Extensions

# EF Core ITenantStore
dotnet add package MicroKit.MultiTenancy.EFCoreStore

# Redis ITenantCache
dotnet add package MicroKit.MultiTenancy.Redis
```

---

## Usage

### Access the current tenant in a handler

```csharp
using MicroKit.MultiTenancy.Abstractions;

public sealed class GetDashboardHandler : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    private readonly ITenantContext _tenant;
    private readonly IReadRepository<Order> _orders;

    public GetDashboardHandler(ITenantContext tenant, IReadRepository<Order> orders) =>
        (_tenant, _orders) = (tenant, orders);

    public async Task<DashboardDto> HandleAsync(GetDashboardQuery query, CancellationToken ct)
    {
        // Throws InvalidOperationException with a clear message if not resolved
        _tenant.EnsureResolved();

        var orders = await _orders.FindAsync(
            o => o.TenantId == _tenant.Tenant!.Id, ct);

        return new DashboardDto(orders.Count, _tenant.Tenant!.Name);
    }
}
```

### ITenant contract

| Property | Type | Description |
|---|---|---|
| `Id` | `string` | Unique tenant identifier |
| `Name` | `string?` | Display name |
| `ConnectionString` | `string?` | Per-tenant connection string — null for shared databases |
| `Items` | `IDictionary<string, object>` | Extensible metadata bag |

### Mark an entity as tenant-owned

```csharp
using MicroKit.MultiTenancy.Abstractions;

public sealed class Invoice : AggregateRoot<Guid>, IHasMultiTenant
{
    public string TenantId { get; private set; } = null!;
    // ... domain fields
}
```

Use `IHasMultiTenant` in your EF Core global query filter:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Invoice>()
        .HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
}
```

### Custom resolution strategy

```csharp
using MicroKit.MultiTenancy.Abstractions;

public sealed class SubdomainResolutionStrategy : ITenantResolutionStrategy
{
    private readonly IHttpContextAccessor _http;

    public SubdomainResolutionStrategy(IHttpContextAccessor http) => _http = http;

    public ValueTask<string?> ResolveAsync(CancellationToken ct = default)
    {
        var host = _http.HttpContext?.Request.Host.Host;
        var subdomain = host?.Split('.').FirstOrDefault();
        return new ValueTask<string?>(subdomain);
    }
}

// Registration
builder.Services.AddSingleton<ITenantResolutionStrategy, SubdomainResolutionStrategy>();
```

### Composite region resolver

```csharp
// Registered via DI — tried in order; first non-null result wins
services.AddScoped<ITenantRegionResolver, ClaimsTenantRegionResolver>();     // first
services.AddScoped<ITenantRegionResolver, DatabaseTenantRegionResolver>();   // fallback
services.AddScoped<ITenantRegionResolver, ConfigurationTenantRegionResolver>(); // last resort

// CompositeTenantRegionResolver is registered automatically by AddMicroKitMultiTenancy
```

---

## Configuration

```csharp
using MicroKit.MultiTenancy.Extensions;

builder.Services
    .AddMicroKitMultiTenancy(options =>
    {
        options.HeaderName            = "X-Tenant-Id";  // used by WithHeaderStrategy
        options.ClaimNames            = "tenant_id";    // used by WithJwtClaimStrategy
        options.EnableValidationWorker = true;           // run IModuleValidator on startup
    })
    .WithHeaderStrategy()       // reads X-Tenant-Id header; override header name in options
    // .WithJwtClaimStrategy()  // reads tenant_id JWT claim; override claim name in options
    .WithRedisCache(opts =>
    {
        opts.ConnectionString = "localhost:6379";
        opts.KeyPrefix        = "tenant:";
        opts.Expiry           = TimeSpan.FromMinutes(15);
    });

var app = builder.Build();

// Resolves the tenant for every request and calls TenantContext.SetTenant once
app.UseMultiTenancy();
```

### appsettings.json

```json
{
  "MicroKit": {
    "MultiTenancy": {
      "HeaderName": "X-Tenant-Id",
      "ClaimNames": "tenant_id",
      "EnableValidationWorker": true
    }
  }
}
```

### Skip tenant resolution for specific endpoints

```csharp
using MicroKit.MultiTenancy.Attributes;

[SkipTenantValidation]
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet] public IActionResult Get() => Ok();
}
```

---

## Key types

| Type | Package | Role |
|---|---|---|
| `ITenantContext` | Abstractions | Read the current tenant; `EnsureResolved()` guard |
| `ITenantContextSetter` | Abstractions | Write the tenant — `SetTenant` is write-once |
| `ITenant` | Abstractions | Resolved tenant: Id, Name, ConnectionString, Items |
| `IHasMultiTenant` | Abstractions | Marker for tenant-filtered EF Core entities |
| `ITenantResolutionStrategy` | Abstractions | Pluggable resolver — returns `string? tenantId` |
| `TenantContext` | Core | Scoped implementation of both `ITenantContext` and `ITenantContextSetter` |
| `HeaderResolutionStrategy` | Extensions | Reads a named HTTP request header |
| `JwtClaimResolutionStrategy` | Extensions | Reads a named JWT claim |
| `TenantResolutionMiddleware` | Extensions | Resolves and sets tenant on every request |
| `MultiTenantValidationService` | Core | Startup `IHostedService` — validates DI wiring |
| `CompositeTenantRegionResolver` | Core | Chains multiple `ITenantRegionResolver` instances |
| `RegionAwareTenantEndpointProvider` | Core | Returns regional service endpoint for a tenant |
| `RedisTenantCache` | Redis | Distributed `ITenantCache` backed by StackExchange.Redis |
| `EFCoreTenantStore` | EFCoreStore | Database-backed `ITenantStore` |
| `SkipTenantValidationAttribute` | Core | Exempts a controller or endpoint from tenant resolution |

---

## Package dependency graph

```
MicroKit.MultiTenancy.Abstractions
    MicroKit.Abstractions
    (no third-party NuGet dependencies)

MicroKit.MultiTenancy
    MicroKit.MultiTenancy.Abstractions
    Microsoft.Extensions.Caching.Memory
    Microsoft.Extensions.Hosting

MicroKit.MultiTenancy.Extensions
    MicroKit.MultiTenancy
    Microsoft.AspNetCore.Http

MicroKit.MultiTenancy.EFCoreStore
    MicroKit.MultiTenancy
    Microsoft.EntityFrameworkCore

MicroKit.MultiTenancy.Redis
    MicroKit.MultiTenancy
    StackExchange.Redis
```
