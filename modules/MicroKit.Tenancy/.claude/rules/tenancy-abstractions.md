# Rule: Abstractions Minimality — MicroKit.Tenancy

## Core principle
`MicroKit.Tenancy.Abstractions` contains **only what a consuming module needs to compile**.
No EF Core, no ASP.NET Core, no AsyncLocal implementation.

## What belongs in Abstractions

```csharp
// ✅ Tenant identity
TenantId                         // sealed record VO — wraps Guid
ITenantInfo                      // Id, Name, ConnectionString?, SchemaName?, IsActive

// ✅ Context contracts
ITenantContext                   // CurrentTenant: ITenantInfo?
ITenantContextAccessor           // GetTenant() / SetTenant() / CreateScope()

// ✅ Resolution contracts
ITenantResolutionStrategy        // TryResolveAsync → Result<TenantId>
ITenantResolver                  // ResolveAsync → Result<ITenantInfo>

// ✅ Store and provisioning contracts
ITenantStore                     // FindAsync, ListAllAsync
ITenantProvisioner               // ProvisionAsync → Result<TenantId>
TenantProvisioningRequest        // sealed record — provisioning params
TenantProvisionedEvent           // sealed record — domain event

// ✅ EF Core marker (interface only — no DbContext dependency)
ITenantEntity                    // TenantId { get; } — marker interface
```

## What does NOT belong in Abstractions

```
❌ AsyncLocalTenantContextAccessor  → Core (implementation)
❌ TenantResolutionPipeline         → Core (implementation)
❌ InMemoryTenantStore              → Core (implementation)
❌ TenantResolutionMiddleware       → AspNetCore
❌ *Strategy implementations        → AspNetCore
❌ EfTenantFilter, interceptors     → EntityFrameworkCore
❌ DbContext, IQueryable, EF types  → never in Abstractions
❌ FrameworkReference               → never in Abstractions
❌ NSubstitute, Shouldly            → test packages, never in production code
```

## Allowed package references in Abstractions

```xml
<!-- ✅ only MicroKit.Result (cross-module ProjectRef in dev, PackageRef in release) -->
<PackageReference Include="MicroKit.Result" />
```

`MicroKit.Result` is required because `ITenantResolver.ResolveAsync` returns `Result<ITenantInfo>`.
No other non-BCL package reference is allowed in Abstractions.

## The minimality test

Ask: "Could a project that does NOT use ASP.NET Core reference Abstractions and compile?"
If the answer is NO, the type is in the wrong package.

## Detecting violations

```bash
# ASP.NET Core leaked into Abstractions
grep -rn 'HttpContext\|IHttpContextAccessor\|Microsoft.AspNetCore' \
  modules/MicroKit.Tenancy/src/MicroKit.Tenancy.Abstractions/ --include="*.cs" --include="*.csproj"

# EF Core leaked into Abstractions
grep -rn 'EntityFrameworkCore\|DbContext\|IQueryable\|ModelBuilder' \
  modules/MicroKit.Tenancy/src/MicroKit.Tenancy.Abstractions/ --include="*.cs" --include="*.csproj"

# Non-BCL package refs in Abstractions .csproj
grep 'PackageReference' \
  modules/MicroKit.Tenancy/src/MicroKit.Tenancy.Abstractions/MicroKit.Tenancy.Abstractions.csproj
```
