# Dependency Graph — MicroKit.Multitenancy

## Within-module graph

```
MicroKit.Multitenancy.Abstractions
  └─ (no MicroKit deps — MicroKit.Result via PackageReference)

MicroKit.Multitenancy (Core)
  └─ MicroKit.Multitenancy.Abstractions

MicroKit.Multitenancy.AspNetCore
  └─ MicroKit.Multitenancy (Core)
  └─ Microsoft.AspNetCore.App (FrameworkReference)

MicroKit.Multitenancy.EntityFrameworkCore
  └─ MicroKit.Multitenancy (Core)
  └─ MicroKit.Persistence.Abstractions
  └─ MicroKit.Persistence.EntityFrameworkCore
  └─ Microsoft.EntityFrameworkCore

MicroKit.Multitenancy.Analyzers
  └─ Microsoft.CodeAnalysis.CSharp (build-time; netstandard2.0)
```

## Cross-module graph (MicroKit ecosystem)

```
Level 0: MicroKit.Result, MicroKit.Domain
Level 1: MicroKit.Logging, MicroKit.Caching
Level 2: MicroKit.Persistence, MicroKit.MediatR
Level 3: MicroKit.Multitenancy  ← this module
```

MicroKit.Multitenancy.Abstractions depends on:
- `MicroKit.Result` (Result<T> for resolution and provisioning returns)

MicroKit.Multitenancy.EntityFrameworkCore depends on:
- `MicroKit.Persistence.Abstractions` (IAggregateRoot constraint for ITenantEntity on aggregates)
- `MicroKit.Persistence.EntityFrameworkCore` (EfUnitOfWork interop; TenantStampInterceptor chains with existing interceptors)

## Forbidden dependencies

| From | Forbidden |
|------|-----------|
| Abstractions | MicroKit.MediatR, MicroKit.Logging, EF Core, ASP.NET Core |
| Core | MicroKit.MediatR, EF Core, ASP.NET Core HTTP types |
| Any | FluentAssertions, circular deps, Level 3 → Level 3 cross-module |

## Phase 2 planned additions

```
MicroKit.Multitenancy.Http       ← DelegatingHandler for outbound tenant propagation
                                    depends on: Core + Microsoft.Extensions.Http
MicroKit.Multitenancy.Messaging  ← tenant header propagation in message pipelines
                                    depends on: Core + MicroKit.Messaging (Level 3→3 via optional bridge)
```
