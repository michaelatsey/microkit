# Context: Dependency Graph

Project and package references of MicroKit.Persistence as the `.csproj` files declare them.
Update it with every `<ProjectReference>` or `<PackageReference>` change; `dependency-guardian`
checks against it. Rules: `.claude/rules/dependencies.md`.

---

## Projects (8 src)

```
MicroKit.Persistence.Abstractions
│   └── cross-module: MicroKit.Domain   (two-ItemGroup: ProjectReference / PackageReference on CIReleaseBuild)
│
MicroKit.Persistence (core)
│   ├── → Abstractions
│   └── cross-module: MicroKit.Result   (two-ItemGroup; the Result path, #197 phase 3 — ADR-011)
│
MicroKit.Persistence.EntityFrameworkCore
│   ├── → core
│   └── NuGet: Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational,
│              Microsoft.Extensions.DependencyInjection.Abstractions
│
MicroKit.Persistence.EntityFrameworkCore.PostgreSql
│   ├── → EntityFrameworkCore
│   └── NuGet: Npgsql.EntityFrameworkCore.PostgreSQL
│
MicroKit.Persistence.EntityFrameworkCore.SqlServer
│   ├── → EntityFrameworkCore
│   └── NuGet: Microsoft.EntityFrameworkCore.SqlServer
│
MicroKit.Persistence.Specifications
│   └── → core
│
MicroKit.Persistence.Testing
│   └── → core
│
MicroKit.Persistence.Analyzers            (netstandard2.0)
    └── NuGet: Microsoft.CodeAnalysis.CSharp (PrivateAssets="all": not a package dependency)
```

Intra-module references are unconditional `ProjectReference` (ADR-GLOBAL-002 D5).
`PostgreSql`, `SqlServer`, `Specifications` and `Testing` are siblings — none references another.

## Tests and samples

| Project | References |
|---------|-----------|
| `UnitTests` | Specifications, Testing |
| `IntegrationTests` | EntityFrameworkCore, PostgreSql, SqlServer (+ MicroKit.Domain); SQLite, Npgsql, Testcontainers.PostgreSql, Testcontainers.MsSql |
| `ArchitectureTests` | every src project except Analyzers; NetArchTest.Rules, Mono.Cecil |
| `Analyzers.Tests` | Analyzers; Microsoft.CodeAnalysis.CSharp.Analyzer.Testing |
| `samples/MicroKit.Persistence.Samples` | EntityFrameworkCore, PostgreSql, Specifications, Testing, Analyzers (as analyzer) |

All test projects use xUnit, Shouldly; UnitTests and IntegrationTests also NSubstitute.

---

## Cross-module

Outgoing (allowed by the root graph: Result, Domain — both are used):

```
MicroKit.Persistence.Abstractions → MicroKit.Domain   (IAggregateRoot, IEntityId; Domain types used by
                                                       core and EntityFrameworkCore come through it)
MicroKit.Persistence (core)       → MicroKit.Result   (IResultUnitOfWork, PersistenceErrors; EntityFrameworkCore,
                                                       Testing and the providers get it through core)
```

Abstractions never references `MicroKit.Result` (#196 D17; `LayerDependencyTests`).

Incoming:

```
MicroKit.MediatR.Behaviors          → Persistence.Abstractions                 (TransactionBehavior)
MicroKit.Tenancy.EntityFrameworkCore → Persistence.Abstractions + .EntityFrameworkCore
MicroKit.MediatR.IntegrationTests   → Persistence.EntityFrameworkCore           (test only)
```

Forbidden: any reference from this module to MediatR (Core and EntityFrameworkCore are checked
against MediatR.Contracts by `LayerDependencyTests`), Messaging, Tenancy or Http; EF Core in
Abstractions or core (ADR-003).

---

## Package versions

All versions in the root `Directory.Packages.props`.

| Package | Used by |
|---------|---------|
| `MicroKit.Domain` | Abstractions (release build) |
| `MicroKit.Result` | core (release build) |
| `Microsoft.EntityFrameworkCore`, `.Relational` | EntityFrameworkCore |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | EntityFrameworkCore |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql, IntegrationTests |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer |
| `NSubstitute` | UnitTests, IntegrationTests |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers, IntegrationTests |

The provider classifiers use the ADO.NET exception types through their EF Core provider package, with
no direct reference: `Npgsql.PostgresException` (PostgreSql, from `Npgsql.EntityFrameworkCore.PostgreSQL`)
and `Microsoft.Data.SqlClient.SqlException` (SqlServer, from `Microsoft.EntityFrameworkCore.SqlServer`).
Intentional: the ADO.NET provider's version is the one the EF Core provider was built against.
