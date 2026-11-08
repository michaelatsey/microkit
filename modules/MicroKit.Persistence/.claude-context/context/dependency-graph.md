# Context: Dependency Graph

**Current state of all project dependencies within MicroKit.Persistence.**

Updated whenever a `<ProjectReference>` or significant `<PackageReference>` is added.
The `dependency-guardian` agent validates against this graph.

---

## Project Reference Graph (8 projects)

```
MicroKit.Persistence.Abstractions
│   └── [no project references — package refs only]
│   └── Cross-module: MicroKit.Domain, MicroKit.Result (removed by #196 D17)
│
MicroKit.Persistence (core)
│   └── → MicroKit.Persistence.Abstractions
│
MicroKit.Persistence.EntityFrameworkCore
│   ├── → MicroKit.Persistence (core)
│   └── NuGet: Microsoft.EntityFrameworkCore,
│              Microsoft.Extensions.DependencyInjection.Abstractions
│
MicroKit.Persistence.EntityFrameworkCore.PostgreSql
│   ├── → MicroKit.Persistence.EntityFrameworkCore
│   └── NuGet: Npgsql.EntityFrameworkCore.PostgreSQL
│
MicroKit.Persistence.EntityFrameworkCore.SqlServer
│   ├── → MicroKit.Persistence.EntityFrameworkCore
│   └── NuGet: Microsoft.EntityFrameworkCore.SqlServer
│
MicroKit.Persistence.Specifications
│   ├── → MicroKit.Persistence (core)
│   └── NuGet: (none beyond core transitive deps)
│
MicroKit.Persistence.Testing
│   ├── → MicroKit.Persistence (core)
│   └── NuGet: NSubstitute
│
MicroKit.Persistence.Analyzers
│   └── [no project references]
│   └── NuGet: Microsoft.CodeAnalysis.CSharp (build-time only)
```

> `PostgreSql`, `SqlServer`, `Specifications`, and `Testing` are **siblings** — none references another.

---

## Package Confinement

| Package | Confined to | Forbidden elsewhere |
|---------|-------------|---------------------|
| `Microsoft.EntityFrameworkCore` | EntityFrameworkCore | Abstractions, Core |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql | All others |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer | All others |
| `NSubstitute` | Testing | All runtime packages |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers | All runtime packages |
| `FluentAssertions` | **banned everywhere** | Commercial license (Xceed EULA) |

---

## Cross-Module Dependencies (Ecosystem)

MicroKit.Persistence is a **Level 2** module:

```
Level 0: Domain · Result
Level 1: Logging → Result(opt) | Caching → Result | Auth → Result+Domain
Level 2: MediatR → Result+Domain+Logging.Abs | Persistence → Domain (+Result until #196 D17)
```

Allowed cross-module references (via PackageReference, never ProjectReference):

```
MicroKit.Persistence.Abstractions → MicroKit.Domain  (IAggregateRoot generic constraint)
MicroKit.Persistence.Abstractions → MicroKit.Result  (a global using only — removed by #196 D17, phase 2)
```

**Incoming cross-module reference:**
```
MicroKit.MediatR.Behaviors → MicroKit.Persistence.Abstractions (ITransactionalContext for TransactionBehavior)
```
This is the intended direction — MediatR.Behaviors is Level 2, it may depend on another Level 2
module's Abstractions. Full cross-module integration: `.claude-context/context/transaction-behavior-integration.md`.

**Forbidden:**
- Dependency on Level 3 modules (Messaging, Http, Multitenancy)
- `ProjectReference` crossing module boundaries — use `PackageReference`
- `Microsoft.EntityFrameworkCore` in Abstractions or Core (ADR-003)

---

## MicroKit.Result Dependency

> **Superseded by #196 D17.** The original reasoning — "`MicroKit.Result` is referenced from
> Abstractions because repository methods in consuming handlers commonly return `Result<T>`; a
> permanent, justified production dependency" — did not hold: no Abstractions type uses
> `MicroKit.Result`, only a `global using` does. Phase 2 removes the reference; #197 phase 3 adds it to
> `MicroKit.Persistence.EntityFrameworkCore`, where a `Result`-returning path will live.

---

## NuGet Package Versions

> Canonical versions in the root `Directory.Packages.props`.

| Package | Used By | Notes |
|---------|---------|-------|
| `MicroKit.Domain` | Abstractions | `IAggregateRoot` constraint |
| `MicroKit.Result` | Abstractions | Removed by #196 D17 (unused) |
| `Microsoft.EntityFrameworkCore` | EntityFrameworkCore | ORM |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql | Provider |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer | Provider |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | EntityFrameworkCore | DI registration |
| `NSubstitute` | Testing | Test mocks |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers | Roslyn API |
