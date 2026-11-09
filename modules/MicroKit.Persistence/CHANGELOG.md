# Changelog — MicroKit.Persistence

## [Unreleased]

### #197 phase 2 — optimistic concurrency

Every aggregate root carries an application-managed version, bumped by every save that writes any
entity of its aggregate. ADR-010 records the decision.

#### Breaking Changes

##### MicroKit.Persistence.Abstractions
- `IRepository<TAggregate, TId>` gains `ValueTask<TAggregate?> FindAsync(TId id, long expectedVersion, CancellationToken ct = default)` and `long GetVersion(TAggregate aggregate)`. Every implementation must add them; `EfRepository` and `InMemoryRepository` do.

##### MicroKit.Persistence.EntityFrameworkCore
- On a context configured with `UseMicroKitPersistence()`, every aggregate root gets a shadow `long` concurrency token, `AggregateVersion`. **A migration is needed**: it adds a `NOT NULL` column to every root's table (EF Core scaffolds `defaultValue: 0L`), so existing rows start at 0 and their first change takes them to 1. The column name follows the model's naming conventions and may be configured.
- The name `AggregateVersion` is reserved on aggregate roots: model building fails when a root maps it to a CLR member, gives it a type other than `long`, stops it being a concurrency token, or maps another property to its column in the root's table.
- `SaveChanges` bumps the version of every root whose boundary has an `Added`, `Modified` or `Deleted` entity, so two commands changing different children of one aggregate now conflict (`PersistenceException` wrapping `DbUpdateConcurrencyException`, nothing of the second save written). A save that changes only a child now also issues an `UPDATE` of the root's version.
- `DeleteAsync` followed by a commit now fails with that conflict when another command changed the aggregate in between (`DELETE … WHERE AggregateVersion = @original`); before, the delete won.
- Adding or removing a many-to-many link bumps the aggregate that declares the navigation, even when its target is outside the boundary.
- A save fails with `InvalidOperationException` when a changed child inside a boundary has no tracked root — including a child moved to or from an aggregate whose root is not tracked — and when a child moves between two aggregates that were not both loaded or staged by their repository. Entities outside every boundary are ignored.

#### Added
- `EfRepository.FindAsync(id, expectedVersion)`: the expected version becomes the token's original value, checked in the root's `UPDATE` or `DELETE`. A stale version conflicts even when the command changes nothing. On a staged root not inserted yet it is checked at the find: anything but 0 throws `PersistenceException`.
- `EfRepository.GetVersion(aggregate)`; an `Added` root reads 0 until its commit writes 1, and a failed save puts the version back.
- `AggregateVersion.PropertyName`, for projections (`EF.Property<long>(o, AggregateVersion.PropertyName)`).
- `InMemoryRepository.SetVersion(aggregate, version)`; the double checks the expected version at the find.

#### Known limits
- `ExecuteUpdate`, `ExecuteDelete` and raw SQL bypass the change tracker: they neither bump the version nor conflict with it.
- Interceptors the application adds with `AddInterceptors` run after MicroKit's version step: a change they make during `SavingChanges` is not bumped.

### #197 phase 1 — aggregate boundaries

The boundary of an aggregate lives in the model, and a partial aggregate can no longer be tracked
through a repository. ADR-009 records the decision; it amends ADR-002 and ADR-007 and reverses
#196 D2.

#### Breaking Changes

##### MicroKit.Persistence (Core)
- `QueryOptions<T>.AsNoTrackingEnabled`, `QueryOptionsExtensions.WithTracking()` and `QueryOptionsExtensions.AsNoTracking()` are removed: the read side never tracks. Load what a command modifies through `IRepository<TAggregate, TId>`. Remove `AsNoTracking()` calls on `QueryOptions` (they restated the default); replace `WithTracking()` reads by a write-side load.

##### MicroKit.Persistence.EntityFrameworkCore
- `EfRepository.IncludeAggregate` is no longer `protected virtual`: no repository can redefine the boundary. A typed finder calls the new `protected LoadAggregatesAsync(Expression<Func<TAggregate, bool>> predicate, CancellationToken ct = default)`, which loads, tracks and records whole aggregates; it takes a predicate, so a finder cannot add a filtered or partial include. Exclude reference data from the boundary with `IsOutsideAggregate()` instead of overriding the includes.
- `EfRepository` requires a context configured with `UseMicroKitPersistence()`; without it, `FindAsync` and `LoadAggregatesAsync` throw `InvalidOperationException`. `AddDbContext`, `UsePostgreSQL` and `UseSqlServer` call it; a context built with `new DbContextOptionsBuilder<T>()`, `OnConfiguring` or a plain `services.AddDbContext` must call it.
- `EfRepository.FindAsync` and `LoadAggregatesAsync` refuse, with an `InvalidOperationException` naming the root type, a tracked root that the repository did not load whole and that is not `Added` — a root tracked by a direct `DbSet` query, a filtered `Include`, an explicit load, `Attach`, `Update`, or a reattachment after `ChangeTracker.Clear()`. #196 returned such a root as is. A root is recorded only once the load that returned it has completed (a load that fails midway, or a root of the same hierarchy auto-included by the model, records nothing); a root staged by `AddAsync` is recorded and stays returnable after the commit.
- On a context configured with `UseMicroKitPersistence()` (a plain `services.AddDbContext` is unaffected), model building fails, naming the types, when a non-root entity type lies inside the boundary of two aggregate roots (directly, or through a base type and one of its derived types). Each ownership of an owned type is its own entity type and is not shared. Two derived types declaring same-named navigations to different types now fail at model building rather than at the first load. An owned navigation marked `IsOutsideAggregate()` fails model building too. So does, per row, a many-to-one reference held by the parent (a non-unique foreign key) or a many-to-many navigation that reaches a non-root entity: make the target an aggregate root, or mark the navigation `IsOutsideAggregate()`; a one-to-one with a unique foreign key stays in the boundary.
- **Reverses #196 D2** — `EfReadRepository.FindAsync`, `ListAsync`, `ListPagedAsync`, `AnyAsync` and `CountAsync` never consult the change tracker: `FindAsync` no longer returns the instance the unit of work tracks, but a new untracked one loaded from the database (a pending change of the command is not visible through it). An `AsTracking()` inside an `Includes` or `OrderBy` delegate no longer makes a read track.

##### MicroKit.Persistence.EntityFrameworkCore.PostgreSql, .SqlServer
- `UsePostgreSQL` and `UseSqlServer` call `UseMicroKitPersistence()` on the context they register: the boundary validation applies to its model.

#### Added
- `UseMicroKitPersistence()` on `DbContextOptionsBuilder` and `DbContextOptionsBuilder<TContext>` (`MicroKitPersistenceDbContextOptionsExtensions`): registers the boundary validation and the scoped record of the roots loaded whole in the context's internal service provider. The record is reset by `DiscardChanges`, by a failed `ExecuteAsync` attempt, and when a pooled context returns to its pool.
- `IsOutsideAggregate()` on `NavigationBuilder` (`AggregateBoundaryNavigationBuilderExtensions`): sets the model annotation `MicroKit:OutsideAggregate`; the navigation is neither loaded with the aggregate nor counted as part of it. Reference, collection and skip navigations.

#### Fixed
- `EfReadRepository.FindAsync` checks each key value against its key property's CLR type and throws `ArgumentException`, as `DbSet.Find` does; the check used to come from the change-tracker lookup that the read side no longer performs.

#### Tests
- `DockerRequiredFact` / `DockerRequiredTheory` skip only when no Docker daemon answers `GET /_ping`: a leftover socket file no longer makes the database tests fail instead of skipping.
- A SQL Server fixture (`Testcontainers.MsSql`) runs next to the PostgreSQL one; every phase 1 scenario runs on both.

Issue #196 settles the module before 2.0.0 stable. The entries below are grouped by package; the
decision number (D1–D17) points at the issue. D3's presumption was confirmed by a test (a transient
commit failure was not retried) before the retry was fixed; D7 was dropped — a drain already runs
exactly one `DetectChanges`. The README is new and documents composition and every public type; its samples
compile in `samples/MicroKit.Persistence.Samples`.

### Build
- The Release restore no longer fails on `System.Security.Cryptography.Pkcs` 9.0.4 (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx), pinned to 9.0.21 (#161).
- References between the module's own projects are unconditional `ProjectReference`s in every build mode (ADR-GLOBAL-002 D5).

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
- `QueryOptionsExtensions.OrderBy(Func<IQueryable<T>, IOrderedQueryable<T>>)` is renamed `WithOrdering`. Named `OrderBy`, it lost to the `QueryOptions<T>.OrderBy` property and could only be called statically (`QueryOptionsExtensions.OrderBy(opts, …)`); replace that call with `opts.WithOrdering(…)`.

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
- The package no longer depends on `NSubstitute`, which none of its code used. A test project that used NSubstitute only through it must now reference `NSubstitute` itself.

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
- **D15** — `UsePostgreSQL<TContext>` and `UseSqlServer<TContext>` take an optional `ProviderOptions`: `Retry = new RetryOnFailureOptions(maxRetryCount, maxRetryDelay)` turns on the provider's retrying execution strategy, `PoolSize` registers the context with `AddDbContextPool`. Both are off by default; existing calls are unchanged. With `PoolSize` set, registration throws `ArgumentException` unless the context's only public constructor takes just `DbContextOptions` or `DbContextOptions<TContext>`: a pooled context's constructor-injected scoped services are resolved once from the root provider and shared by every lease, so tenant-aware contexts such as `MultitenantDbContext` must not be pooled.
- **D13** — `DomainConventionExtensions` for `ConfigureConventions`: `AddEntityIdConventions(params Assembly[])` maps every concrete `EntityId<T>` of the given assemblies, and `GuidId`, to a `Guid` column, reading values back through the type's `(Guid)` constructor whatever its accessibility (model building fails, naming the type, when there is none); `AddActorConvention()` maps `Actor` to a string of at most `Actor.MaxLength` characters read back through `Actor.Restore` (on an existing schema the 200-character limit is a column change to migrate); `AddDomainConventions(params Assembly[])` applies all three conventions.
- **D14** — `AddAuditInstantConvention()` normalises the `CreatedAt` / `UpdatedAt` instants of every `IAuditableEntity` to offset zero on read and write (query parameters included), and rejects a stored `default` instant on read. Compiled models (`dotnet ef dbcontext optimize`) and NativeAOT precompiled queries are not supported by the D13/D14 conventions: the generated code instantiates internal converters.
- **D6** — `EfCoreBuilder.ConfigurePagination(PaginationLimits)`; `AddEntityFrameworkCore()` registers `PaginationLimits.Default`.
- `EfDomainEventsProvider<TContext>` — change-tracker-backed `IDomainEventsProvider` that aggregates domain events across every entity tracked by `TContext`. Candidates are detected on `IHasDomainEvents` (the read contract); draining requires `IDomainEventsProvider`, so an entity that exposes events without implementing the drain contract is reported by `DomainEvents` but skipped by `DrainDomainEvents`. Reads the in-memory change tracker only — never queries the database.

#### MicroKit.Persistence.Testing
- `InMemoryUnitOfWork.DiscardChanges()` and `DiscardCount`, symmetric with `CommitCount`, so tests can assert that a failed command discarded exactly once.

### Fixed

#### MicroKit.Persistence.EntityFrameworkCore
- **D14** — an audit instant read from a row stored with a non-zero offset (SQL Server `datetimeoffset`, SQLite text) kept that offset, and a later write to a PostgreSQL `timestamptz` column failed ("only offset 0 is supported"); a row holding `0001-01-01` loaded silently. With `AddAuditInstantConvention()`, the first is normalised and the second rejected (#172).
- **D1** — a query with `IncludeDeleted = true` returned other tenants' rows (the parameterless `IgnoreQueryFilters()` disabled the tenant filter). An architecture test now fails on any library call to it.
- **D2** — reading an aggregate the current command had added, modified or deleted through `EfReadRepository.FindAsync` detached it, and the next commit silently wrote nothing. Loading an untracked one could also wire it into tracked entities' navigations before detaching it, so a later commit tried to insert it.
- **D3** — a transient failure of the command's `SaveChangesAsync` inside `ExecuteAsync` was never retried: `CommitAsync`'s `PersistenceException` hid the `DbUpdateException` from the execution strategy. A failure of the transaction's COMMIT left the attempt's accepted changes tracked, so a retry could apply them twice.
- **D3** — with a provider's default (non-retrying) strategy, `CommitAsync` leaked `InvalidOperationException` for a transient write failure instead of `PersistenceException`.
- **D15** — with a retrying strategy, `CommitAsync` called outside `ExecuteAsync` leaked `RetryLimitExceededException` once the retries were exhausted, instead of the documented `PersistenceException`.
- **D4** — `ExecuteAsync` inside an open transaction (nested, or caller-owned) threw `InvalidOperationException` ("The connection is already in a transaction").
- **D5** — the rollback after a failed operation ran with the caller's token, so after a cancellation it threw `OperationCanceledException` and masked the original exception; a failing rollback masked it too.
- `ExecuteAsync` let a raw `DbUpdateException` escape when the operation flushed through the `DbContext` itself rather than `CommitAsync`, instead of the documented `PersistenceException`.
- `EfReadRepository` list, paged list, any and count queries with `WithTracking()` ran untracked on a context configured with `QueryTrackingBehavior.NoTracking`, so changes to the aggregates they returned were silently lost at commit.

#### MicroKit.Persistence.Analyzers
- The package declared `Microsoft.CodeAnalysis.CSharp` as a NuGet dependency, so every consumer restored Roslyn as a package. The reference is now `PrivateAssets="all"`: the compiler that loads the analyzer provides Roslyn, and the nuspec declares no dependency.

#### MicroKit.Persistence.Testing
- `InMemoryReadRepository.FindAsync` and `InMemoryRepository.FindAsync(object[])` threw `ArgumentNullException` for a null key value, where `EfReadRepository.FindAsync` returns `null`.

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
