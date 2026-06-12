# Rule: Dependencies — MicroKit.Persistence

## Central Package Management (CPM)

All NuGet versions live in `build/Directory.Packages.props`. Never set `Version=` on a
`PackageReference` in a `.csproj`.

## The 8-Project Layer Graph

```
MicroKit.Persistence.Abstractions      ← IRepository, IReadRepository, IUnitOfWork,
        ↑                                ITransactionalContext, IPagedResult, PersistenceException
MicroKit.Persistence (core)            ← ISpecificationEvaluator, QueryOptions, pagination, conventions
        ↑
MicroKit.Persistence.EntityFrameworkCore  ← EfRepository, EfUnitOfWork, EfSpecificationEvaluator,
        ↑                                   ITransactionalUnitOfWork
MicroKit.Persistence.EntityFrameworkCore.PostgreSql  ← Npgsql provider
MicroKit.Persistence.EntityFrameworkCore.SqlServer   ← SqlServer provider
MicroKit.Persistence.Specifications    ← QueryOptions extensions, spec helpers (sibling of EFCore)
MicroKit.Persistence.Testing           ← InMemoryRepository, test helpers (sibling of EFCore)
MicroKit.Persistence.Analyzers         ← build-time only (no runtime dependency chain)
```

## Allowed Package Matrix

| Project | Allowed Packages |
|---------|-----------------|
| `Abstractions` | `MicroKit.Result`, `MicroKit.Domain.Abstractions` |
| `Core` | Abstractions (project) + `MicroKit.Logging.Abstractions` |
| `EntityFrameworkCore` | Core (project) + `Microsoft.EntityFrameworkCore` + `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `PostgreSql` | EntityFrameworkCore (project) + `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `SqlServer` | EntityFrameworkCore (project) + `Microsoft.EntityFrameworkCore.SqlServer` |
| `Specifications` | Core (project) — no extra packages |
| `Testing` | Core (project) + `NSubstitute` |
| `Analyzers` | `Microsoft.CodeAnalysis.CSharp` (build-time attribute only) |

## Project Reference Rules

```
Abstractions   → (no project references — package refs only)
Core           → Abstractions
EntityFrameworkCore → Core
PostgreSql     → EntityFrameworkCore
SqlServer      → EntityFrameworkCore
Specifications → Core
Testing        → Core
Analyzers      → (no project references — standalone Roslyn package)
```

### Sibling Isolation
`Specifications`, `Testing`, `PostgreSql`, and `SqlServer` are siblings — none references another.
`Testing` must not reference `Specifications` and vice versa.

## Cross-Module Dependencies (Ecosystem)

MicroKit.Persistence is a **Level 2** module:

```
MicroKit.Persistence.Abstractions → MicroKit.Result              (production)
MicroKit.Persistence.Abstractions → MicroKit.Domain.Abstractions (IAggregateRoot constraint)
MicroKit.Persistence (core)       → MicroKit.Logging.Abstractions (structured log property names)
```

> Any addition to Abstractions requires explicit `api-reviewer` approval.

**Forbidden:**
- Dependency on Level 3 modules (Messaging, Http, Multitenancy)
- `ProjectReference` crossing module boundaries (use `PackageReference`)
- `Microsoft.EntityFrameworkCore` in Abstractions or Core

## Adding a New Dependency

1. Check it is not already available transitively
2. Add version to `Directory.Packages.props`
3. Verify it targets `.NET 10` / `netstandard2.0` / `netstandard2.1`
4. Confirm it lands in the correct layer
5. Any addition to `Abstractions`: requires explicit `api-reviewer` approval

## Package Confinement

| Package | Confined to | Forbidden elsewhere |
|---------|-------------|---------------------|
| `Microsoft.EntityFrameworkCore` | EntityFrameworkCore | Abstractions, Core |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql | All others |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer | All others |
| `NSubstitute` | Testing | All runtime packages |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers | All runtime packages |
| `FluentAssertions` | **banned everywhere** | Commercial license (Xceed EULA) |
