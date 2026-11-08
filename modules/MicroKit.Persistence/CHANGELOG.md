# Changelog — MicroKit.Persistence

## [Unreleased]

Issue #196 settles the module before 2.0.0 stable. The entries below are grouped by package; the
decision number (D1–D17) points at the issue.

### Breaking Changes

#### MicroKit.Persistence.Abstractions
- **D10** — `IRepository<TAggregate>` is replaced by `IRepository<TAggregate, TId> where TAggregate : class, IAggregateRoot where TId : IEntityId`, the only write-side contract: `FindAsync(TId id, ct)` loads the whole aggregate, tracked; `AddAsync`; `DeleteAsync`.
- **D9** — `IRepository.CommitAsync` is removed: committing belongs to `IUnitOfWork` only (`TransactionBehavior` calls it).
- **D11** — `IRepository.UpdateAsync` is removed: an aggregate loaded through `FindAsync` is tracked, so its changes need no call; `DbSet.Update` marked a whole graph `Modified` and was unsafe on a detached one.
- **D8** — the empty `MicroKit.Persistence.Abstractions.IReadRepository<TAggregate>` marker is removed; `MicroKit.Persistence.IReadRepository<TAggregate>` (Core) is the only read contract (#120).
- **D12** — `ITransactionManager` and `ITransaction` are removed: implemented and used nowhere.
- **D17** — the package no longer depends on `MicroKit.Result`, which it did not use.

#### MicroKit.Persistence (Core)
- **D1** — `QueryOptions<T>.IncludeDeleted` and `QueryOptionsExtensions.IncludeSoftDeleted()` are removed. They called the parameterless `IgnoreQueryFilters()`, which disables **every** query filter of the model — a tenant filter included — while the module owns no soft delete. #197 reintroduces the flag on a *named* filter.
- **D6** — `PaginationOptions` is no longer a positional record: `new PaginationOptions(page, pageSize)` validates `page >= 1`, `pageSize >= 1` and that the offset fits in an `int`, and `Page`, `PageSize`, `Skip` are get-only (no `Deconstruct`, no `with { Page = … }`). Named arguments change from `Page:`/`PageSize:` to `page:`/`pageSize:`.

#### MicroKit.Persistence.EntityFrameworkCore
- **D10** — `EfRepository<TAggregate, TContext>(TContext, IUnitOfWork)` becomes `EfRepository<TAggregate, TId, TContext>(TContext)`. The protected `FindByKeyAsync(object[])`, which loaded the root without its children, is removed; `FindAsync(TId)` loads the whole aggregate (every navigation to a non-root entity, recursively; split when it has two or more collections), and the protected virtual `IncludeAggregate(IQueryable<TAggregate>)` lets a typed repository apply or replace those includes.
- **D9, D11** — `EfRepository.CommitAsync` and `UpdateAsync` are removed.
- **D12** — `EfTransaction` is removed.
- **D6** — `EfReadRepository<TAggregate, TContext>` takes a third, required constructor parameter `PaginationLimits paginationLimits`; typed subclasses forward it (the container resolves the registered instance).
- **D6** — `ListPagedAsync` without pagination returns page 1 of `PaginationLimits.DefaultPageSize` (50) items instead of the whole result set; a requested page size above `PaginationLimits.MaxPageSize` (500) throws `ArgumentOutOfRangeException` from `ListAsync` and `ListPagedAsync`.
- **D2** — `EfReadRepository.FindAsync` returns an aggregate the unit of work already tracks as is (same instance, same state) instead of detaching it, and loads any other aggregate with a no-tracking query instead of tracking then detaching it.
- **D3–D5** — `EfUnitOfWork.ExecuteAsync` joins an open `Database.CurrentTransaction`; exhausted retries surface as `PersistenceException` (inner `RetryLimitExceededException`) instead of `RetryLimitExceededException`; failures of the transaction itself surface as `PersistenceException`; a failed attempt clears the change tracker. See ADR-006.

#### MicroKit.Persistence.Testing
- **D10** — `InMemoryRepository<TAggregate>` becomes `InMemoryRepository<TAggregate, TId>`: `FindAsync(TId)` replaces `FindById(object)`; `UpdateAsync` and `CommitAsync` are removed; the key-selector constructors take a `Func<TAggregate, TId>`.
- **D6** — `InMemoryRepository<T>` and `InMemoryReadRepository<T>` enforce `PaginationLimits` like the EF Core read repository (new constructor overloads take one); `ListPagedAsync` without pagination returns the default page.

#### MicroKit.Persistence.Abstractions
- `IUnitOfWork` gains `void DiscardChanges()` — abandons the pending change set without writing it. Every implementer must add the member. Consumers that only inject `IUnitOfWork` are unaffected: the member is called by `TransactionBehavior`, not by handlers.

  ```csharp
  public sealed class MyUnitOfWork(MyContext context) : IUnitOfWork
  {
      public ValueTask CommitAsync(CancellationToken ct = default) => /* unchanged */;

      public void DiscardChanges() => context.ChangeTracker.Clear();   // EF Core
      // public void DiscardChanges() { }                              // Dapper / raw SQL — no pending set
  }
  ```

  It is called at every command boundary that does not commit — business failure *and* thrown exception, since a transaction rollback does not reset the change tracker. Rationale, alternatives, and the full consequence list are in ADR-005 (`.claude-context/context/architectural-decisions.md`).

### Added

#### MicroKit.Persistence (Core)
- **D6** — `PaginationLimits` (default page 50, maximum 500), validated on construction.

#### MicroKit.Persistence.EntityFrameworkCore
- **D6** — `EfCoreBuilder.ConfigurePagination(PaginationLimits)`; `AddEntityFrameworkCore()` registers `PaginationLimits.Default`.
- `EfDomainEventsProvider<TContext>` — change-tracker-backed `IDomainEventsProvider` that aggregates domain events across every entity tracked by `TContext`. Candidates are detected on `IHasDomainEvents` (the read contract); draining requires `IDomainEventsProvider`, so an entity that exposes events without implementing the drain contract is reported by `DomainEvents` but skipped by `DrainDomainEvents`. Reads the in-memory change tracker only — never queries the database.

#### MicroKit.Persistence.Testing
- `InMemoryUnitOfWork.DiscardChanges()` and `DiscardCount`, symmetric with `CommitCount`, so tests can assert that a failed command discarded exactly once.

### Fixed

#### MicroKit.Persistence.EntityFrameworkCore
- **D1** — a query with `IncludeDeleted = true` returned other tenants' rows (the parameterless `IgnoreQueryFilters()` disabled the tenant filter). An architecture test now fails on any library call to it.
- **D2** — reading an aggregate the current command had added, modified or deleted through `EfReadRepository.FindAsync` detached it, and the next commit silently wrote nothing. Loading an untracked one could also wire it into tracked entities' navigations before detaching it, so a later commit tried to insert it.
- **D3** — a transient failure of the command's `SaveChangesAsync` inside `ExecuteAsync` was never retried: `CommitAsync`'s `PersistenceException` hid the `DbUpdateException` from the execution strategy. A failure of the transaction's COMMIT left the attempt's accepted changes tracked, so a retry could apply them twice.
- **D3** — with a provider's default (non-retrying) strategy, `CommitAsync` leaked `InvalidOperationException` for a transient write failure instead of `PersistenceException`.
- **D4** — `ExecuteAsync` inside an open transaction (nested, or caller-owned) threw `InvalidOperationException` ("The connection is already in a transaction").
- **D5** — the rollback after a failed operation ran with the caller's token, so after a cancellation it threw `OperationCanceledException` and masked the original exception; a failing rollback masked it too.

#### MicroKit.Persistence.Abstractions
- A command that failed its business rule could persist its data anyway. `TransactionBehavior` correctly skipped the flush on failure, but the entities the handler staged stayed in the scoped `DbContext`'s change tracker; the next successful command in the same scope wrote them along with its own. Identical on the exception path — EF Core's transaction rollback does not reset the change tracker. The failure was silent: no error, no log, and nothing distinguishing it from correct operation. `IUnitOfWork` had no way to express "abandon this change set", so no consumer could fix it without reaching past the abstraction into EF Core.

#### MicroKit.Persistence.EntityFrameworkCore
- Domain-event dispatchers that depend on `IDomainEventsProvider` could not be activated. No package registered an implementation, and none could correctly be supplied by a consumer: the contract is aggregate-level — `AggregateRoot<TId>` is its only implementer, draining its own events — while the drain phase requires the events of *all* tracked aggregates, so a DI-resolved provider could only ever be one arbitrary aggregate. Resolution failed with `Unable to resolve service for type 'MicroKit.Domain.Events.IDomainEventsProvider'`. `AddUnitOfWork<TContext>()` now also registers a unit-of-work-scoped provider.

---

## [1.0.0-preview.3] — 2026-06-25
### Fixed
#### MicroKit.Persistence.Abstractions
- Corrected `MicroKit.Domain` dependency constraint to `>= 1.0.0-preview.5` — previous nuspec declared `>= 1.0.0-preview.2` due to missing `CIReleaseBuild=true` on pack step, causing NuGet to resolve an incompatible Domain version transitively.

---

## [1.0.0-preview.2] — 2026-06-22

### Added

#### MicroKit.Persistence.Abstractions
- `ITransactionalContext` — ambient transaction execution contract (`ExecuteAsync`)

### Changed

#### MicroKit.Persistence.EntityFrameworkCore
- `EfUnitOfWork` — implements `ITransactionalContext`

### Removed

#### MicroKit.Persistence.Abstractions
- `IOutboxStore` — removed. The contract was orphaned: no EF Core implementation existed, no consumer referenced it, and its `Add(INotification)` signature incorrectly pulled `MediatR.Contracts` into a package described as "Zero implementation — no infrastructure dependency." The outbox pattern will be rebuilt in `MicroKit.Messaging.Abstractions`, typed on `IIntegrationEvent`, when `MicroKit.Messaging` is implemented. The `MediatR.Contracts` `PackageReference` has been removed from the project file.
- `MediatR.Contracts` `PackageReference` removed from the project file
- `global using MediatR` removed

---

## [1.0.0-preview.1] — 2026-06-01

### Packages Released

- `MicroKit.Persistence.Abstractions`
- `MicroKit.Persistence`
- `MicroKit.Persistence.EntityFrameworkCore`
- `MicroKit.Persistence.EntityFrameworkCore.PostgreSql`
- `MicroKit.Persistence.EntityFrameworkCore.SqlServer`
- `MicroKit.Persistence.Specifications`
- `MicroKit.Persistence.Testing`
- `MicroKit.Persistence.Analyzers`

### Added

#### MicroKit.Persistence.Abstractions
- `IRepository<TAggregate>` — write-side repository contract with `FindAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `CommitAsync` (Unit of Work boundary)
- `IReadRepository<TAggregate>` — read-side repository contract with `FindAsync`, `ListAsync`, `AnyAsync`, `CountAsync` (no mutations, analyzer-enforced)
- `IUnitOfWork` — single-method commit boundary (`CommitAsync`), moved from `MicroKit.Domain` (ADR-001)
- `ITransactionalContext` + `ITransaction` + `ITransactionManager` — ambient transaction lifecycle contracts
- `IPagedResult<T>` — paged result contract with `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`
- `PersistenceException` — typed exception wrapping provider-level failures

#### MicroKit.Persistence (Core)
- `QueryOptions<TAggregate>` — separates WHAT (Specification) from HOW (includes, tracking, pagination, split queries) per ADR-002
- `ISpecificationEvaluator` — applies `QueryOptions<T>` to `IQueryable<T>`; lives in Core, not Abstractions (ADR-003)
- `PagedResult<T>` — `IPagedResult<T>` sealed record implementation
- `PaginationOptions` — Page + PageSize value record with computed Skip
- `IOutboxStore` — stores `INotification` (MediatR.Contracts) domain/integration events for outbox pattern
- `TransactionBehavior` integration support via `ITransactionalContext` (from Abstractions)

#### MicroKit.Persistence.EntityFrameworkCore
- `EfRepository<TAggregate, TContext>` — `IRepository<T>` EF Core implementation with `AsTracking`, change-tracker-aware writes
- `EfReadRepository<TAggregate, TContext>` — `IReadRepository<T>` EF Core implementation; all queries enforced `AsNoTracking`
- `EfUnitOfWork<TContext>` — `ITransactionalUnitOfWork` implementation wrapping `DbContext.SaveChangesAsync` + `Database.BeginTransactionAsync`
- `EfSpecificationEvaluator` — canonical `ISpecificationEvaluator` applying criteria → includes → split → order → paginate
- `ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext` — EF Core composite (ADR-004); not in Abstractions
- `AddMicroKitPersistence` + `AddEntityFrameworkCore` DI extension methods with builder pattern

#### MicroKit.Persistence.EntityFrameworkCore.PostgreSql
- `UsePostgreSQL(string connectionString)` extension on `EfCoreBuilder`
- Npgsql provider configuration (retry-on-failure, connection resiliency)

#### MicroKit.Persistence.EntityFrameworkCore.SqlServer
- `UseSqlServer(string connectionString)` extension on `EfCoreBuilder`
- SqlServer provider configuration (retry-on-failure, connection resiliency)

#### MicroKit.Persistence.Specifications
- `WithOrderBy` / `WithOrderByDescending` / `WithThenBy` / `WithThenByDescending` — ordering extensions on `QueryOptions<T>` (With- prefix avoids delegate-vs-extension collision with the `OrderBy` property)
- `WithSpec` — specification-swap extension
- Specification composition re-exported from `MicroKit.Domain`: `And`, `Or`, `Not`

#### MicroKit.Persistence.Testing
- `InMemoryRepository<TAggregate>` — in-memory `IRepository<T>` for unit tests; no EF Core required
- `InMemoryReadRepository<TAggregate>` — in-memory `IReadRepository<T>` with spec criteria evaluation via LINQ
- `InMemoryUnitOfWork` — `IUnitOfWork` test double; tracks `CommitAsync` call count
- Full Shouldly + NSubstitute test coverage (46/46 tests)

#### MicroKit.Persistence.Analyzers
- `MKP001` (Error) — `IReadRepository<T>` implementation calls `CommitAsync` or `SaveChangesAsync`
- `MKP002` (Error) — `IReadRepository<T>` implementation declares or calls a write method (`AddAsync`, `UpdateAsync`, `DeleteAsync`)
- `MKP003` (Warning) — `SaveChangesAsync` / `SaveChanges` called directly on `DbContext` (bypasses `IUnitOfWork`)
- `MKP004` (Warning) — `DbContext` injected as constructor parameter outside infrastructure namespace
- `MKP005` (Error) — Repository method exposes `IQueryable<T>` as return type (leaks EF internals to app layer)
- `PersistenceSymbolHelper` with null-guard convention (safe against compilations missing the Persistence packages)
- Build-time only package (`analyzers/dotnet/cs/` — no `lib/` folder); 26/26 tests

#### MicroKit.Persistence.ArchitectureTests
- `LayerDependencyTests` (20 tests) — layer isolation: EF Core confined to EfCore/providers; Npgsql/SqlServer confined to their provider; `MediatR.Contracts` in Abstractions only; sibling isolation
- `NamespaceConventionTests` (9 tests) — correct namespaces per assembly across all 7 assemblies + both providers; static extension classes in correct namespaces
- `SealedClassTests` (10 tests) — `TypeAttributes.Sealed` for open-generic types (`EfUnitOfWork<>`, `EfRepository<,>`); non-sealed base confirmation
- `ContractPlacementTests` (15 tests) — ADR-001 through ADR-004 enforced at architecture level; `IReadRepository` marker in Abstractions + full contract in Core; `IOutboxStore` placement
- `ReadRepositoryPurityTests` (5 tests) — marker interface has 0 methods; Core interface has no mutations; `IQueryable<T>` not exposed on `InMemoryRepository`, `InMemoryReadRepository`, `EfReadRepository`
- Total: 59/59 tests

### Architecture Decisions

- **ADR-001** — `IUnitOfWork` moved from `MicroKit.Domain` to `MicroKit.Persistence.Abstractions`. Committing is an infrastructure concern. Breaking change for consumers importing `IUnitOfWork` from `MicroKit.Domain`.
- **ADR-002** — `QueryOptions<T>` separates WHAT (`Specification<T>` — domain) from HOW (includes, tracking, pagination — application). `Specification<T>` contains criteria only; no `Include`, no pagination.
- **ADR-003** — Abstractions minimality rule. `MicroKit.Persistence.Abstractions` contains only what a consuming module needs to compile without EF Core. `ISpecificationEvaluator` and `QueryOptions<T>` live in Core.
- **ADR-004** — `ITransactionalUnitOfWork` is the EF Core composite (`IUnitOfWork + ITransactionalContext`). It is declared in `EntityFrameworkCore`, not in Abstractions. Provider-agnostic code injects the two interfaces separately.

### Test Summary

| Suite | Tests |
|-------|-------|
| `MicroKit.Persistence.UnitTests` | (integration of in-memory repos + QueryOptions) |
| `MicroKit.Persistence.Analyzers.Tests` | 26/26 |
| `MicroKit.Persistence.ArchitectureTests` | 59/59 |
| Total (non-integration) | 131+ |

### Notes — Deferred to v1.1.0

- **DN-001** — Execution strategy gap in `EfUnitOfWork.CommitAsync`: direct `CommitAsync` calls (bypassing `TransactionBehavior`) do not retry on transient failures for cloud databases (Azure SQL, Neon). The `TransactionBehavior` path via `ExecuteAsync` is fully protected. For v1.1.0: wrap `CommitAsync` in `CreateExecutionStrategy()` with a guard against nested strategies when a transaction is already active.
- **DN-002** — `ListPagedAsync` fallback `PageSize: 1` when `totalCount == 0`: the paging math is correct (`TotalPages = 0`) but consumers inspecting `PageSize` on an empty unpaginated result may find `PageSize = 1` confusing. Risk is low. For v1.1.0: consider `PagedResult.Empty<T>()` factory or a `<remarks>` note on `ListPagedAsync`.
