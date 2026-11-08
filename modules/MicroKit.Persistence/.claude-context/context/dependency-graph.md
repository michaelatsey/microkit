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
│   └── → Abstractions
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
│   ├── → core
│   └── NuGet: NSubstitute
│
MicroKit.Persistence.Analyzers            (netstandard2.0)
    └── NuGet: Microsoft.CodeAnalysis.CSharp
```

Intra-module references are unconditional `ProjectReference` (ADR-GLOBAL-002 D5).
`PostgreSql`, `SqlServer`, `Specifications` and `Testing` are siblings — none references another.

## Tests and samples

| Project | References |
|---------|-----------|
| `UnitTests` | Specifications, Testing |
| `IntegrationTests` | EntityFrameworkCore, PostgreSql, SqlServer (+ MicroKit.Domain); SQLite, Npgsql, Testcontainers.PostgreSql |
| `ArchitectureTests` | every src project except Analyzers; NetArchTest.Rules, Mono.Cecil |
| `Analyzers.Tests` | Analyzers; Microsoft.CodeAnalysis.CSharp.Analyzer.Testing |
| `samples/MicroKit.Persistence.Samples` | EntityFrameworkCore, PostgreSql, Specifications, Testing, Analyzers (as analyzer) |

All test projects use xUnit, Shouldly; UnitTests and IntegrationTests also NSubstitute.

---

## Cross-module

Outgoing (allowed by the root graph: Result, Domain — only Domain is used):

```
MicroKit.Persistence.Abstractions → MicroKit.Domain   (IAggregateRoot, IEntityId; Domain types used by
                                                       core and EntityFrameworkCore come through it)
```

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
| `Microsoft.EntityFrameworkCore`, `.Relational` | EntityFrameworkCore |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | EntityFrameworkCore |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql, IntegrationTests |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer |
| `NSubstitute` | Testing, UnitTests, IntegrationTests |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers, IntegrationTests |
