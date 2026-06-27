# Rule: Dependencies — MicroKit.Tenancy

## Central Package Management (CPM)

All NuGet versions live in `Directory.Packages.props` at the monorepo root.
Never set `Version=` on a `PackageReference` in a `.csproj`.

## The 5-Project Layer Graph

```
MicroKit.Tenancy.Abstractions      ← ITenantContext, ITenantContextAccessor, ITenantInfo,
        ↑                                  TenantId, ITenantResolver, ITenantResolutionStrategy,
        ↑                                  ITenantStore, ITenantProvisioner, TenantProvisioningRequest,
        ↑                                  TenantProvisionedEvent, ITenantEntity
MicroKit.Tenancy (core)            ← AsyncLocalTenantContextAccessor, TenantResolutionPipeline,
        ↑                                  InMemoryTenantStore, ConfigurationTenantStore, DI registration
MicroKit.Tenancy.AspNetCore        ← TenantResolutionMiddleware, HTTP strategies
MicroKit.Tenancy.EntityFrameworkCore ← TenantStampInterceptor, query filter helpers, bypass scope
MicroKit.Tenancy.Analyzers         ← build-time only (no runtime dependency chain)
```

## Allowed Package Matrix

| Project | Allowed Packages |
|---------|-----------------|
| `Abstractions` | `MicroKit.Result` |
| `Core` | Abstractions (project) + `Microsoft.Extensions.DependencyInjection.Abstractions` + `Microsoft.Extensions.Options` + `Microsoft.Extensions.Logging.Abstractions` |
| `AspNetCore` | Core (project) + `FrameworkReference Microsoft.AspNetCore.App` |
| `EntityFrameworkCore` | Core (project) + `MicroKit.Persistence.Abstractions` + `MicroKit.Persistence.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore` |
| `Analyzers` | `Microsoft.CodeAnalysis.CSharp` (build-time attribute only) |

## Project Reference Rules

```
Abstractions    → (no project references — package refs only)
Core            → Abstractions (ProjectReference in dev, PackageReference in CIReleaseBuild)
AspNetCore      → Core (ProjectReference in dev, PackageReference in CIReleaseBuild)
EntityFrameworkCore → Core + Persistence packages (ProjectReference in dev)
Analyzers       → (no project references — standalone Roslyn package)
```

## Cross-Module Dependencies

MicroKit.Tenancy is a **Level 3** module:

```
MicroKit.Tenancy.Abstractions         → MicroKit.Result (production)
MicroKit.Tenancy (core)               → MicroKit.Tenancy.Abstractions
MicroKit.Tenancy.EntityFrameworkCore  → MicroKit.Persistence.Abstractions
                                              MicroKit.Persistence.EntityFrameworkCore
                                              Microsoft.EntityFrameworkCore
```

**Forbidden:**
- `MicroKit.MediatR` in any project (cross-level — MediatR is Level 2, can integrate via optional package Phase 2)
- `ProjectReference` crossing module boundaries from `src/`
- `Microsoft.EntityFrameworkCore` in Abstractions or Core
- `IHttpContextAccessor` in Abstractions or Core
- `FluentAssertions` anywhere

## Adding a New Dependency

1. Verify not already available transitively
2. Add version to `Directory.Packages.props`
3. Confirm `.NET 10` / `netstandard2.0` / `netstandard2.1` compatibility
4. Verify it lands in the correct layer
5. Any addition to `Abstractions` → requires `api-reviewer` approval

## Package Confinement

| Package | Confined to | Forbidden elsewhere |
|---------|-------------|---------------------|
| `Microsoft.EntityFrameworkCore` | EntityFrameworkCore | Abstractions, Core |
| `Microsoft.AspNetCore.App` | AspNetCore | All others |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers | All runtime packages |
| `FluentAssertions` | **banned everywhere** | Commercial license (Xceed EULA) |
