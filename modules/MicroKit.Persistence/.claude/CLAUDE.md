# MicroKit.Persistence — Module Brain

## Purpose

An opinionated persistence layer for .NET 10: repository and Unit of Work contracts, an EF Core
implementation, `QueryOptions` over `MicroKit.Domain` specifications, and Roslyn analyzers that
keep the CQRS data-access split honest.

> The domain defines what is persisted; this module defines how. Command handlers load and stage
> aggregates through `IRepository<TAggregate, TId>` and commit through `IUnitOfWork`; query
> handlers read through `IReadRepository<TAggregate>`.

Consumer documentation: `README.md`. Every C# sample in it lives in, and builds from,
`samples/MicroKit.Persistence.Samples`. Decisions: `.claude-context/context/architectural-decisions.md`
(ADR-001 to ADR-009, DN-001, DN-002).

---

## Navigation

| Task | Load first | Agent |
|------|-----------|-------|
| Implementing anything new | this file + the relevant rule | `implementer` — plans, waits for approval |
| Architecture / contract decision | `rules/architecture.md` + `architectural-decisions.md` | `architect` |
| Adding a repository | `workflows/adding-repository.md` | `implementer` → `test-generator` |
| Adding a provider | `workflows/adding-provider.md` | `implementer` → `ef-core-specialist` |
| Adding a specification | `workflows/adding-specification.md` + `rules/specifications.md` | `implementer` |
| EF Core concern (mapping, query, transaction) | `rules/ef-core-patterns.md` | `ef-core-specialist` |
| Analyzer (MKP001–MKP005) | `rules/analyzers.md` | `analyzer-specialist` |
| Performance concern | `rules/performance.md` + `.claude-context/standards/performance-budget.md` | `performance-reviewer` |
| Public API change | `rules/abstractions.md` + `rules/naming.md` | `api-reviewer` |
| `.csproj` / reference change | `rules/dependencies.md` + `.claude-context/context/dependency-graph.md` | `dependency-guardian` |
| Tests | `rules/testing.md` + `workflows/testing-repositories.md` | `test-generator` |
| Release | `workflows/releasing-module.md` | `release-manager` |

Paths are relative to `.claude/` unless they start with `.claude-context/`. The module has no
slash commands; the root ones (`/release`, `/audit-dependencies`, `/bootstrap-module-claude`,
`/new-module`) apply.

---

## Structure

```
MicroKit.Persistence/
├── src/
│   ├── MicroKit.Persistence.Abstractions/        IRepository<TAggregate, TId>, IUnitOfWork,
│   │                                              ITransactionalContext, IPagedResult<T>, PersistenceException
│   ├── MicroKit.Persistence/                      IReadRepository<T>, QueryOptions<T> + extensions,
│   │                                              PaginationOptions, PaginationLimits, PagedResult<T>,
│   │                                              ISpecificationEvaluator
│   ├── MicroKit.Persistence.EntityFrameworkCore/  EfRepository, EfReadRepository, EfUnitOfWork,
│   │                                              ITransactionalUnitOfWork, EfSpecificationEvaluator,
│   │                                              EfDomainEventsProvider, DI builders, ProviderOptions,
│   │                                              domain conventions (ids, Actor, audit instants)
│   ├── MicroKit.Persistence.EntityFrameworkCore.PostgreSql/  UsePostgreSQL<TContext>
│   ├── MicroKit.Persistence.EntityFrameworkCore.SqlServer/   UseSqlServer<TContext>
│   ├── MicroKit.Persistence.Specifications/       WithOrderBy/WithThenBy…, WithSpec
│   ├── MicroKit.Persistence.Testing/              InMemoryRepository<TAggregate, TId>,
│   │                                              InMemoryReadRepository<T>, InMemoryUnitOfWork
│   └── MicroKit.Persistence.Analyzers/            MKP001–MKP005 (netstandard2.0, build-time only)
├── tests/
│   ├── MicroKit.Persistence.UnitTests/            QueryOptions, pagination, in-memory doubles
│   ├── MicroKit.Persistence.IntegrationTests/     SQLite + PostgreSQL + SQL Server (Testcontainers)
│   ├── MicroKit.Persistence.ArchitectureTests/    NetArchTest layering + IL call scan (Mono.Cecil)
│   └── MicroKit.Persistence.Analyzers.Tests/
└── samples/MicroKit.Persistence.Samples/          README code, built in Release with the analyzers
```

There is no benchmark project and no performance-test project.

---

## Dependency Graph

```
MicroKit.Persistence.Abstractions  ← MicroKit.Domain (cross-module, two-ItemGroup pattern)
        ↑
MicroKit.Persistence (core)        ← no package
        ↑
MicroKit.Persistence.EntityFrameworkCore  ← Microsoft.EntityFrameworkCore(.Relational),
        ↑                                   Microsoft.Extensions.DependencyInjection.Abstractions
MicroKit.Persistence.EntityFrameworkCore.PostgreSql  ← Npgsql.EntityFrameworkCore.PostgreSQL
MicroKit.Persistence.EntityFrameworkCore.SqlServer   ← Microsoft.EntityFrameworkCore.SqlServer
MicroKit.Persistence.Specifications  ← core
MicroKit.Persistence.Testing         ← core (no package)
MicroKit.Persistence.Analyzers       ← Microsoft.CodeAnalysis.CSharp, PrivateAssets="all" (no project reference)
```

Incoming: `MicroKit.MediatR.Behaviors` → Abstractions; `MicroKit.Tenancy.EntityFrameworkCore` →
Abstractions + EntityFrameworkCore. Details: `.claude-context/context/dependency-graph.md`.

---

## Key Contracts

```csharp
// Abstractions — write side
IRepository<TAggregate, TId>   // TAggregate : class, IAggregateRoot; TId : IEntityId
    FindAsync(TId id, ct)      // whole aggregate, tracked
    AddAsync(aggregate, ct) · DeleteAsync(aggregate, ct)   // stage only — no commit, no update
IUnitOfWork                    // CommitAsync(ct) · DiscardChanges()
ITransactionalContext          // ExecuteAsync<TState>(op, state, ct) · ExecuteAsync<TState, TResult>(…)

// Core — read side (the only read contract)
IReadRepository<TAggregate>    // FindAsync(object[] keyValues) · ListAsync · ListPagedAsync · AnyAsync · CountAsync
QueryOptions<TAggregate>       // Specification, Includes, Pagination, AsSplitQueryEnabled, OrderBy
                               // (no tracking option: the read side never tracks)
PaginationLimits               // DefaultPageSize 50, MaxPageSize 500; ListPagedAsync applies the default
                               // page, ListAsync and ListPagedAsync reject a size above the maximum

// EntityFrameworkCore
EfRepository<TAggregate, TId, TContext>     // unsealed; protected LoadAggregatesAsync(predicate, ct);
                                            // refuses a tracked root it did not load whole (ADR-009)
EfReadRepository<TAggregate, TContext>(context, evaluator, paginationLimits)  // unsealed
EfUnitOfWork<TContext> : ITransactionalUnitOfWork   // = IUnitOfWork + ITransactionalContext
UseMicroKitPersistence()       // on DbContextOptionsBuilder: boundary convention + AggregateLoadRecord
IsOutsideAggregate()           // on NavigationBuilder: reference data, outside the boundary
```

Composition:

```csharp
services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef => ef   // + ISpecificationEvaluator, PaginationLimits.Default
    .UsePostgreSQL<AppDbContext>(cs, options: new ProviderOptions { Retry = new RetryOnFailureOptions() })
    .AddUnitOfWork<AppDbContext>()                                          // + IDomainEventsProvider
    .ConfigurePagination(new PaginationLimits(20, 100))));
```

`AddDbContext<TContext>` registers a context without a provider package. Typed repositories are
registered by the application. `ConfigureConventions` calls `AddDomainConventions(assemblies)`
(or `AddEntityIdConventions`, `AddActorConvention`, `AddAuditInstantConvention`).

`EfUnitOfWork.ExecuteAsync` runs inside the provider's execution strategy, retries a transient
commit failure, joins a transaction already open on the context, never lets a rollback failure
mask the original exception, and clears the change tracker after a failed attempt — ADR-006.
`TransactionBehavior` (MicroKit.MediatR) calls `ExecuteAsync`, then `CommitAsync` after
dispatching domain events, and `DiscardChanges()` on every non-commit exit (ADR-005).

`Specification<T>` (criteria only) lives in MicroKit.Domain; `QueryOptions<T>` adds the loading
strategy (ADR-002). `IUnitOfWork` moved here from MicroKit.Domain (ADR-001).

---

## Non-Negotiable Rules

1. Commit through `IUnitOfWork.CommitAsync` only; no repository commits, and no public
   `SaveChangesAsync`.
2. `IReadRepository` never commits or mutates (MKP001/MKP002).
3. Read paths never track and never consult the change tracker: every `EfReadRepository` method
   runs `AsNoTracking()` and returns new instances. Only the write side tracks roots, and only
   whole ones: `EfRepository` refuses a tracked root it did not load whole and that is not `Added`.
   The boundary lives in the model only (`IsOutsideAggregate()`, no override) — ADR-009.
4. `Specification<T>` stays in Domain; `ISpecificationEvaluator` and `QueryOptions` stay in core.
5. No EF Core in Abstractions or core: no `DbContext`, `DbSet`, `IQueryable` in Abstractions.
6. Library code never calls the parameterless `IgnoreQueryFilters()` (`IlCallScanTests`).
7. `sealed` for UoW, evaluator, events provider and in-memory doubles; `EfRepository` and
   `EfReadRepository` are unsealed base classes for typed repositories (`SealedClassTests`).
   `sealed record` for `QueryOptions`, `PagedResult`, `PaginationOptions`, `PaginationLimits`,
   `ProviderOptions`.
8. Repository and UoW methods return `ValueTask`; `ITransactionalContext` returns `Task` (frozen).
   `ConfigureAwait(false)` on every await; `CancellationToken ct = default` last.
9. Tests use xUnit + Shouldly + NSubstitute; FluentAssertions is forbidden (root
   `.claude/rules/testing-libraries.md`).
10. Central Package Management: versions in the root `Directory.Packages.props`, no `Version=`.

---

## Agents

| Agent | Model | Use |
|-------|-------|-----|
| `implementer` | Opus | First, before new code — plan, then wait for approval |
| `architect` | Opus | Contracts, UoW, QueryOptions shape, module boundaries |
| `ef-core-specialist` | Opus | EfRepository, EfReadRepository, EfUnitOfWork, conventions, providers |
| `analyzer-specialist` | Opus | MKP diagnostics and their tests |
| `api-reviewer` | Opus | Public surface of Abstractions or core |
| `performance-reviewer` | Sonnet | Query path, round trips, tracking, allocations |
| `dependency-guardian` | Haiku | Any `.csproj` change — PASS/BLOCK |
| `release-manager` | Sonnet | Release of the 8 packages |
| `test-generator` | Sonnet | Shouldly + NSubstitute suites over the in-memory doubles |

---

## Context Layer

`.claude-context/` is loaded explicitly by agents, never by Claude Code itself:

```
.claude-context/
├── context/architectural-decisions.md   ADR-001…ADR-009, DN-001, DN-002
├── context/dependency-graph.md          project and package graph as declared
├── context/ecosystem-overview.md        position among MicroKit modules
├── standards/performance-budget.md      targets (no benchmark project yet)
└── templates/specification-template.md  Specification<T> + its tests
```

---

## Versioning

A `persistence-v<semver>` tag triggers `.github/workflows/release-persistence.yml`, which stamps
all 8 packages with the tag's version (`-p:PackageVersion`) and publishes them. `version.json`
(`1.0.0-preview.3`) is not read by any build: Nerdbank.GitVersioning is not referenced.
ADR-GLOBAL-002 (root) replaces this model; no tag is cut until steps 1–4 of its migration land.
