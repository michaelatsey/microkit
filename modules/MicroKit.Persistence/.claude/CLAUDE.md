# MicroKit.Persistence — Module Brain

## Purpose

An opinionated persistence layer for .NET 10: repository and Unit of Work contracts, an EF Core
implementation, `QueryOptions` over `MicroKit.Domain` specifications, and Roslyn analyzers that
keep the CQRS data-access split honest and flag costly query shapes.

> The domain defines what is persisted; this module defines how. Command handlers load and stage
> aggregates through `IRepository<TAggregate, TId>` and commit through `IUnitOfWork`; query
> handlers read through `IReadRepository<TAggregate>`.

Consumer documentation: `README.md`. Every C# sample in it lives in, and builds from,
`samples/MicroKit.Persistence.Samples`. Decisions: `.claude-context/context/architectural-decisions.md`
(ADR-001 to ADR-015, DN-001, DN-002).

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
| Analyzer (MKP001–MKP009) | `rules/analyzers.md` + ADR-014 | `analyzer-specialist` |
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
│   │                                              ISpecificationEvaluator; the Result path:
│   │                                              IResultUnitOfWork, PersistenceErrors,
│   │                                              AggregateVersionConflictException
│   ├── MicroKit.Persistence.EntityFrameworkCore/  EfRepository, EfReadRepository, EfUnitOfWork,
│   │                                              ITransactionalUnitOfWork, EfSpecificationEvaluator,
│   │                                              EfDomainEventsProvider, DI builders, ProviderOptions,
│   │                                              domain conventions (ids, Actor, audit instants)
│   ├── MicroKit.Persistence.EntityFrameworkCore.PostgreSql/  UsePostgreSQL<TContext>, PostgreSqlFailureClassifier
│   ├── MicroKit.Persistence.EntityFrameworkCore.SqlServer/   UseSqlServer<TContext>, SqlServerFailureClassifier
│   ├── MicroKit.Persistence.Specifications/       WithOrderBy/WithThenBy…, WithSpec
│   ├── MicroKit.Persistence.Testing/              InMemoryRepository<TAggregate, TId>,
│   │                                              InMemoryReadRepository<T>, InMemoryUnitOfWork, DockerProbe
│   ├── MicroKit.Persistence.Testing.PostgreSql/   PostgreSqlFixture (Testcontainers, IAsyncDisposable)
│   ├── MicroKit.Persistence.Testing.SqlServer/    SqlServerFixture (Testcontainers, IAsyncDisposable)
│   └── MicroKit.Persistence.Analyzers/            MKP001–MKP009 (netstandard2.0, build-time only)
├── tests/
│   ├── MicroKit.Persistence.UnitTests/            QueryOptions, pagination, PersistenceErrors, in-memory doubles,
│   │                                              the container lifecycle and the Docker probe
│   ├── MicroKit.Persistence.IntegrationTests/     SQLite + PostgreSQL + SQL Server (the Testing.* fixtures)
│   ├── MicroKit.Persistence.ArchitectureTests/    NetArchTest layering + IL call scan (Mono.Cecil)
│   └── MicroKit.Persistence.Analyzers.Tests/
└── samples/MicroKit.Persistence.Samples/          README code, built in Release with the analyzers
```

There is no benchmark project and no performance-test project.

---

## Dependency Graph

```
MicroKit.Persistence.Abstractions  ← MicroKit.Domain (cross-module, unconditional ProjectReference)
        ↑
MicroKit.Persistence (core)        ← MicroKit.Result (cross-module, unconditional ProjectReference)
        ↑
MicroKit.Persistence.EntityFrameworkCore  ← Microsoft.EntityFrameworkCore(.Relational),
        ↑                                   Microsoft.Extensions.DependencyInjection.Abstractions
MicroKit.Persistence.EntityFrameworkCore.PostgreSql  ← Npgsql.EntityFrameworkCore.PostgreSQL
MicroKit.Persistence.EntityFrameworkCore.SqlServer   ← Microsoft.EntityFrameworkCore.SqlServer
MicroKit.Persistence.Specifications  ← core
MicroKit.Persistence.Testing         ← core (no package; MicroKit.Result through core)
        ↑
MicroKit.Persistence.Testing.PostgreSql  ← Testcontainers.PostgreSql (no test framework, no EF Core)
MicroKit.Persistence.Testing.SqlServer   ← Testcontainers.MsSql
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
    FindAsync(TId id, long expectedVersion, ct) · GetVersion(aggregate)   // ETag / If-Match (ADR-010)
    AddAsync(aggregate, ct) · DeleteAsync(aggregate, ct)   // stage only — no commit, no update
IUnitOfWork                    // CommitAsync(ct) · DiscardChanges()
ITransactionalContext          // ExecuteAsync<TState>(op, state, ct) · ExecuteAsync<TState, TResult>(…)

// Core — the Result path (ADR-011): conflicts as Result errors, converted after the rollback
IResultUnitOfWork              // TryExecuteAsync(op, state, ct) ×2 · TryCommitAsync(ct) — EfUnitOfWork, InMemoryUnitOfWork
PersistenceErrors              // PERSISTENCE.<ROOT>.<DUPLICATE|REFERENCE|CONCURRENCY>, category Conflict
AggregateVersionConflictException  // inner of a version conflict a repository detects itself

// Core — read side (the only read contract)
IReadRepository<TAggregate>    // FindAsync(object[] keyValues) · ListAsync · ListPagedAsync · ListKeysetAsync
                               // (each also with a projection selector) · AnyAsync · CountAsync — ADR-013
QueryOptions<TAggregate>       // Specification, Includes, Pagination, AsSplitQueryEnabled, OrderBy,
                               // IncludeDeleted (WithDeleted), Keyset (WithKeyset); no tracking option
KeysetPagination<TAggregate>   // OrderBy/ThenBy[Descending] keys, PageSize, After; the root's key appended;
                               // forward only. KeysetCursor: opaque token, ToString/TryParse; KeysetPage<T>
PaginationLimits               // DefaultPageSize 50, MaxPageSize 500; ListPagedAsync applies the default
                               // page, ListAsync and ListPagedAsync reject a size above the maximum

// EntityFrameworkCore
EfRepository<TAggregate, TId, TContext>     // unsealed; protected LoadAggregatesAsync(predicate, ct);
                                            // refuses a tracked root it did not load whole (ADR-009)
EfReadRepository<TAggregate, TContext>(context, evaluator, paginationLimits)  // unsealed
EfUnitOfWork<TContext> : ITransactionalUnitOfWork, IResultUnitOfWork   // = IUnitOfWork + ITransactionalContext
UseMicroKitPersistence()       // on DbContextOptionsBuilder: boundary convention + AggregateLoadRecord
IsOutsideAggregate()           // on NavigationBuilder: reference data, outside the boundary
AggregateVersion.PropertyName  // shadow long concurrency token on every root, bumped by every save
                               // that writes its boundary (AggregateSaveInterceptor, ADR-010)
HasAggregateErrorName(name)    // on EntityTypeBuilder: the <ROOT> of the PERSISTENCE codes (ADR-011)
IDatabaseFailureClassifier     // provider error codes; PostgreSqlFailureClassifier, SqlServerFailureClassifier
UseMicroKitPersistence(classifier)  // UsePostgreSQL / UseSqlServer pass theirs
HasSoftDelete()                // on the root's EntityTypeBuilder: deleting the root marks the whole aggregate
                               // (SoftDelete.PropertyName AggregateDeletedAt, FilterName MicroKit.SoftDelete — ADR-012)

// Testing (ADR-015) — no test framework
DockerProbe.IsAvailable        // Testing: GET /_ping answered 200; cached, never throws; BCL only
PostgreSqlFixture, SqlServerFixture  // Testing.PostgreSql / .SqlServer: one container; StartAsync(ct) once, a caller's
                               // token cancels only its wait · ConnectionString · GetConnectionString(database) · DisposeAsync
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
7. `sealed` for UoW, evaluator, events provider, in-memory doubles and the relational fixtures; `EfRepository` and
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
| `release-manager` | Sonnet | Release of the 10 packages |
| `test-generator` | Sonnet | Shouldly + NSubstitute suites over the in-memory doubles |

---

## Context Layer

`.claude-context/` is loaded explicitly by agents, never by Claude Code itself:

```
.claude-context/
├── context/architectural-decisions.md   ADR-001…ADR-014, DN-001, DN-002
├── context/dependency-graph.md          project and package graph as declared
├── context/ecosystem-overview.md        position among MicroKit modules
├── standards/performance-budget.md      targets (no benchmark project yet)
└── templates/specification-template.md  Specification<T> + its tests
```

---

## Versioning

A `persistence-v<semver>` tag triggers `.github/workflows/release-persistence.yml`, which stamps
all 10 packages with the tag's version (`-p:PackageVersion`) and publishes them. `version.json`
(`1.0.0-preview.3`) is not read by any build: Nerdbank.GitVersioning is not referenced.
ADR-GLOBAL-002 (root) replaces this model; no tag is cut until steps 1–4 of its migration land.
