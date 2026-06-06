# Rule: Naming — MicroKit.Multitenancy

## Value Objects and Identifiers

| Type | Convention | Example |
|---|---|---|
| Tenant identifier VO | `TenantId` | `sealed record TenantId(Guid Value)` |
| Tenant info | `ITenantInfo` | interface |
| Provisioning params | `TenantProvisioningRequest` | `sealed record` |
| Domain event | `TenantProvisionedEvent` | `sealed record` |

## Context and Accessor

| Type | Convention | Example |
|---|---|---|
| Context interface | `ITenantContext` | |
| Accessor interface | `ITenantContextAccessor` | |
| AsyncLocal implementation | `AsyncLocalTenantContextAccessor` | `sealed class` |
| Context scope | `TenantScope` | `sealed class : IDisposable` (private nested) |

## Resolution Pipeline

| Type | Convention | Example |
|---|---|---|
| Strategy interface | `ITenantResolutionStrategy` | |
| HTTP strategy | `{Source}TenantResolutionStrategy` | `HeaderTenantResolutionStrategy`, `RouteDataTenantResolutionStrategy`, `SubdomainTenantResolutionStrategy`, `ClaimsTenantResolutionStrategy`, `HostTenantResolutionStrategy` |
| Resolver | `ITenantResolver` / `TenantResolver` | |
| Pipeline | `TenantResolutionPipeline` | `sealed class` implementing `ITenantResolver` |
| Middleware | `TenantResolutionMiddleware` | `sealed class` |

## Store and Provisioning

| Type | Convention | Example |
|---|---|---|
| Store interface | `ITenantStore` | |
| In-memory store | `InMemoryTenantStore` | `sealed class` |
| Config-based store | `ConfigurationTenantStore` | `sealed class` |
| Provisioner interface | `ITenantProvisioner` | |

## EF Core

| Type | Convention | Example |
|---|---|---|
| Entity marker | `ITenantEntity` | interface with `TenantId { get; }` |
| DbContext marker | `ITenantDbContext` | interface |
| Interceptor | `TenantStampInterceptor` | `sealed class : SaveChangesInterceptor` |
| Bypass scope | `IgnoreTenantScope` | `sealed class : IDisposable` |

## Analyzers

| Type | Convention | Example |
|---|---|---|
| Analyzer class | `{Concern}Analyzer` | `TenantEntityAnalyzer`, `QueryFilterBypassAnalyzer`, `SingletonTenantAccessorAnalyzer` |
| Diagnostic ID | `MKT{NNN}` | `MKT001`, `MKT002`, `MKT003` |

## DI Extension Methods

```csharp
// ✅ Entry point
AddMicroKitMultitenancy(this IServiceCollection, Action<MultitenancyBuilder>?)
AddAspNetCoreResolution(this MultitenancyBuilder)
UseMultitenancy(this IApplicationBuilder)
AddEntityFrameworkCoreIsolation(this MultitenancyBuilder, Action<EfIsolationBuilder>?)
```

## Namespaces

```csharp
namespace MicroKit.Multitenancy;                          // Abstractions
namespace MicroKit.Multitenancy;                          // Core (same root — no .Core suffix)
namespace MicroKit.Multitenancy.AspNetCore;               // ASP.NET Core bridge
namespace MicroKit.Multitenancy.EntityFrameworkCore;      // EF Core bridge
namespace MicroKit.Multitenancy.Analyzers;                // Roslyn analyzers
```

## Errors namespace

```csharp
namespace MicroKit.Multitenancy;

public static class MultitenancyErrors
{
    public static readonly Error TenantNotFound = Error.From("TENANT_NOT_FOUND", "...");
    public static readonly Error InvalidTenantId = Error.From("INVALID_TENANT_ID", "...");
    public static readonly Error TenantInactive = Error.From("TENANT_INACTIVE", "...");
}
```
