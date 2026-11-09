# Context: Dependency Graph

Project and package references of MicroKit.Persistence as the `.csproj` files declare them.
Update it with every `<ProjectReference>` or `<PackageReference>` change; `dependency-guardian`
checks against it. Rules: `.claude/rules/dependencies.md`.

---

## Projects (10 src)

```
MicroKit.Persistence.Abstractions
│   └── cross-module: MicroKit.Domain   (unconditional ProjectReference, from source)
│
MicroKit.Persistence (core)
│   ├── → Abstractions
│   └── cross-module: MicroKit.Result   (unconditional ProjectReference, from source; the Result path — ADR-011)
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
│   └── → core   (in-memory doubles, DockerProbe; BCL only, no Testcontainers)
│
MicroKit.Persistence.Testing.PostgreSql
│   ├── → Testing
│   └── NuGet: Testcontainers.PostgreSql
│
MicroKit.Persistence.Testing.SqlServer
│   ├── → Testing
│   └── NuGet: Testcontainers.MsSql
│
MicroKit.Persistence.Analyzers            (netstandard2.0)
    └── NuGet: Microsoft.CodeAnalysis.CSharp (PrivateAssets="all": not a package dependency)
```

Intra-module references are unconditional `ProjectReference` (ADR-GLOBAL-002 D5).
`PostgreSql`, `SqlServer`, `Specifications` and `Testing` are siblings — none references another.
`Testing.PostgreSql` and `Testing.SqlServer` sit above `Testing`, not beside it; they reference no EF Core, no provider
package, not `Specifications` and not each other, and bring no test framework (ADR-015; `LayerDependencyTests`,
`PackageClosureTests`).
Two projects grant `InternalsVisibleTo`; it is not a reference:
- core → `EntityFrameworkCore`, `Testing`, `UnitTests` (the keyset cursor's codec and rules, ADR-013);
- `Testing` → `Testing.PostgreSql`, `Testing.SqlServer` (the shared container lifecycle, ADR-015) and `UnitTests`, a
  test assembly that is never packed (the lifecycle and the Docker probe's seams).

## Tests and samples

| Project | References |
|---------|-----------|
| `UnitTests` | Specifications, Testing (internals: the container lifecycle, the probe) |
| `IntegrationTests` | EntityFrameworkCore, PostgreSql, SqlServer, Testing.PostgreSql, Testing.SqlServer (+ MicroKit.Domain); SQLite, Npgsql (Testcontainers through the fixture packages) |
| `ArchitectureTests` | every src project except Analyzers (the two fixture packages included); NetArchTest.Rules, Mono.Cecil |
| `Analyzers.Tests` | Analyzers; Microsoft.CodeAnalysis.CSharp.Analyzer.Testing |
| `samples/MicroKit.Persistence.Samples` | EntityFrameworkCore, PostgreSql, Specifications, Testing, Testing.PostgreSql, Analyzers (as analyzer) (+ MicroKit.Domain) |

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
| `Microsoft.EntityFrameworkCore`, `.Relational` | EntityFrameworkCore |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | EntityFrameworkCore |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql, IntegrationTests |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer |
| `NSubstitute` | UnitTests, IntegrationTests |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers, IntegrationTests |
| `Microsoft.EntityFrameworkCore.Sqlite` | IntegrationTests |
| `Testcontainers.PostgreSql` | Testing.PostgreSql (IntegrationTests, ArchitectureTests and the samples through it) |
| `Testcontainers.MsSql` | Testing.SqlServer (IntegrationTests and ArchitectureTests through it) |

The provider classifiers use the ADO.NET exception types through their EF Core provider package, with
no direct reference: `Npgsql.PostgresException` (PostgreSql, from `Npgsql.EntityFrameworkCore.PostgreSQL`)
and `Microsoft.Data.SqlClient.SqlException` (SqlServer, from `Microsoft.EntityFrameworkCore.SqlServer`).
Intentional: the ADO.NET provider's version is the one the EF Core provider was built against.
