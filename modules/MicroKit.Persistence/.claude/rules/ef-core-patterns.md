# Rule: EF Core Patterns — MicroKit.Persistence

Applies to the EntityFrameworkCore, PostgreSql and SqlServer projects.

## Model configuration (consumer side)

- One `IEntityTypeConfiguration<T>` per aggregate, applied with `ApplyConfigurationsFromAssembly`;
  no configuration sprawl in `OnModelCreating`.
- MicroKit.Domain types are mapped by conventions, from `ConfigureConventions`:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    => builder.AddDomainConventions(typeof(OrderId).Assembly);
// = AddEntityIdConventions(assemblies)  EntityId<T> and GuidId → Guid column, read back through the (Guid) ctor
//   AddActorConvention()                Actor → string(Actor.MaxLength)
//   AddAuditInstantConvention()         IAuditableEntity CreatedAt/UpdatedAt → UTC instants
```

An explicit `HasConversion` in `OnModelCreating` wins over each convention. Compiled models and
NativeAOT precompiled queries are not supported by them (their converters are internal).

## Write side — `EfRepository<TAggregate, TId, TContext>`

- `FindAsync(TId)` requires a single-property primary key of CLR type `TId`, else
  `InvalidOperationException`.
- Requires `UseMicroKitPersistence()` on the context (`AddDbContext`, `UsePostgreSQL`,
  `UseSqlServer` call it); otherwise `InvalidOperationException`.
- A root the repository loaded whole (recorded in the scoped `AggregateLoadRecord`), or one that
  is `Added`, is returned as is; any other tracked root is refused (`InvalidOperationException`
  naming the type) — ADR-009. Otherwise one tracked query loads the whole aggregate: every
  navigation reachable from the root (owned types come with their owner), stopping at another
  `IAggregateRoot`, at a navigation marked `IsOutsideAggregate()` and at cycles. Two or more
  collections (an owned collection mapped to JSON does not count) switch the load to
  `AsSplitQuery()`: 1 + C round trips for C collections.
- The plan is computed once per model entity type (`AggregateIncludePlan`, private
  `IncludeAggregate`); the boundary is the model's only — no override. A model-finalizing
  convention fails model building when a non-root entity is inside two roots' boundaries, when a
  many-to-one reference held by the parent reaches a non-root entity, or when a many-to-many
  navigation is declared inside a boundary without `IsOutsideAggregate()` (per-row uniqueness; a
  one-to-one with a unique FK is fine; links are a link entity, a child of the root).
- `AggregateReferenceConvention`: a foreign key from inside an aggregate to another root or to an
  entity outside every aggregate is `Restrict`; an explicit `Cascade`/`SetNull` on it fails model
  building. Keys inside one boundary keep their behaviour. A key into a soft-deletable aggregate from
  outside it is `ClientNoAction` instead (ADR-012).
- Custom finders call `protected LoadAggregatesAsync(predicate, ct)`, which loads, tracks,
  records and checks the returned roots after the query (identity resolution):

```csharp
var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
return orders.SingleOrDefault();
```

- `AddAsync` and `DeleteAsync` only stage changes; the unit of work commits.

## Aggregate version — ADR-010

- `AggregateVersionConvention` (first finalizing convention) adds the shadow `long`
  `AggregateVersion` concurrency token to every top-most root; only the name is reserved, the column
  may be configured.
- `AggregateSaveInterceptor` is MicroKit's single `SaveChanges` entry point: it detects changes
  (`SavingChanges` runs before EF Core's `DetectChanges`), then runs its steps in a fixed order; the
  version step (`AggregateVersionStep`) is last — phase 4's soft-delete conversion goes before it.
- Roots are resolved through `AggregateBoundaryMap` with an index over `ChangeTracker.Entries()`:
  dependent children by original FK (deleted, moved out) or current FK; principal-side children by
  the parents' original or current FK; keys compared with EF Core's key value comparers. Entities
  outside every boundary are ignored; a changed child without a tracked root, or a move between aggregates
  not both recorded, fails the save.
- Bump: `Added` → 1, otherwise original + 1, `Deleted` untouched. Do not bump by hand, and do not
  write aggregate rows with `ExecuteUpdate`/raw SQL when the version must hold.

## Persistence failures as `Result` — ADR-011

- `EfUnitOfWork` implements `IResultUnitOfWork`. `TryExecuteAsync` runs `ExecuteAsync`'s core and maps a
  `PersistenceException` only after it has thrown (rolled back, strategy gave up); joined
  (`CurrentTransaction` set) it converts nothing. A failed `Result` from the operation is carried out of
  the strategy by an internal non-transient sentinel (`FailedResultRollback<TResult>`) so the attempt
  rolls back — no flag. `TryCommitAsync` refuses an open transaction.
- `PersistenceFailureMapper` (internal) reads the exception chain, never a message:
  `DbUpdateConcurrencyException` / `AggregateVersionConflictException` → CONCURRENCY; a classified
  `DbException` (under a `DbUpdateException` or bare) → DUPLICATE / REFERENCE; `RetryLimitExceededException`
  → never. `<ROOT>` comes from the failing entries' entity types (`AggregateBoundaryMap`), one root or
  none.
- Provider codes: `IDatabaseFailureClassifier` from the context's options
  (`UseMicroKitPersistence(classifier)`, passed by `UsePostgreSQL`/`UseSqlServer`). SQL Server `547`
  (`ForeignKeyOrCheckViolation`) maps to REFERENCE only when no entity type of the failing entries'
  tables declares a check constraint (design-time model).
- `AggregateErrorNameConvention` (last finalizing convention) annotates each top-most root with
  `MicroKit:ErrorName` (`HasAggregateErrorName` or UPPER_SNAKE of the CLR name) and fails on a clash, on
  the annotation on a non-root, on an invalid default name.
- An ambient `System.Transactions` transaction is rejected (EF Core's own refusals, pinned by tests).

## Soft delete — ADR-012

- `HasSoftDelete()` on a top-most root. `SoftDeleteConvention` (second finalizing convention) gives the root
  type of each hierarchy of the aggregate (owned types follow their owner) the shadow
  `DateTimeOffset? AggregateDeletedAt` and the named filter `MicroKit.SoftDelete`, keeping other named
  filters. It fails on: an anonymous filter; a required boundary link with `Restrict`/`NoAction`/
  `ClientSetNull`/`SetNull` (EF Core throws at `Remove` under `Immediate` timing); the annotation on a
  non-root; the reserved name misused.
- `AggregateSoftDeleteStep` runs before the version step (and before EF Core's `CascadeChanges`): for each
  `Deleted` soft-deletable root (recorded, or the save fails), every tracked entry of its boundary — any
  state, resolved by current keys first (strict for principal-side children), original keys only when the
  current ones name no tracked root; a child moved to a live root is not marked — gets its nulled FK back
  (which clears a conceptual null), `Deleted` → `Unchanged` with changed properties (complex ones too)
  re-flagged, and the mark `TimeProvider.GetUtcNow()` (one per save). Owned entries of a marked entity are
  restored without a mark while their owner holds them; an added replacement that `Immediate` cascade
  detached is tracked again. After the commit the still-tracked entities are detached (the list lives on
  `AggregateLoadRecord`, cleared with it).
- A key into a soft-deletable aggregate from outside it is `ClientNoAction` (`AggregateReferenceConvention`,
  replacing `Restrict`); an explicit other behaviour fails model building.
- `EfSpecificationEvaluator` step 0: `IncludeDeleted` → `IgnoreQueryFilters([SoftDelete.FilterName])`.

## Read side — `EfReadRepository<TAggregate, TContext>`

- `FindAsync(object[] keyValues)`: key count must match the primary key; a null value matches
  nothing; a value of another CLR type than its key property throws `ArgumentException`; the
  aggregate is always loaded `AsNoTracking`, the change tracker is never consulted.
- List, paged list, any and count start from `AsNoTracking()` (re-applied after the evaluator, so an
  `Includes` delegate cannot opt back in), then go through `ISpecificationEvaluator`. Count and
  any drop includes, ordering and pagination.
- `PaginationLimits` bounds every list: `ListPagedAsync` without pagination returns the first
  page of `DefaultPageSize`; a requested size above `MaxPageSize` throws
  `ArgumentOutOfRangeException`.

## `EfSpecificationEvaluator` — fixed order

1. `Where(spec.ToExpression())` 2. `Includes` 3. `AsSplitQuery()` 4. `OrderBy` 5. `Skip/Take`.
Every query transformation goes through it; no inline composition in repositories.

## Query filters

Library code never calls the parameterless `IgnoreQueryFilters()`: it disables every filter,
a tenant filter the module does not own included. `IncludeDeleted` uses the named overload with
`SoftDelete.FilterName` only. `IlCallScanTests` scans the IL of every library
assembly for it. Only the named overload may be used.

## Unit of Work — `EfUnitOfWork<TContext>`

- `CommitAsync` = `SaveChangesAsync`, translated to `PersistenceException`
  (concurrency conflict, update failure, a provider's transient wrapper).
- `DiscardChanges` = `ChangeTracker.Clear()`, plus a reset of the `AggregateLoadRecord`.
- `ExecuteAsync`: execution strategy + transaction per attempt, retry by unwrapping the
  `DbUpdateException`, join of an open transaction, rollback that never masks, tracker cleared
  after a failed attempt — ADR-006. Do not re-implement any of this in a consumer.
- `AddUnitOfWork<TContext>` registers it scoped as `EfUnitOfWork<TContext>`, `IUnitOfWork`,
  `ITransactionalContext`, `ITransactionalUnitOfWork`, `IResultUnitOfWork`, plus
  `EfDomainEventsProvider<TContext>` as `IDomainEventsProvider`.

## Providers

`UsePostgreSQL<TContext>(connectionString, npgsqlOptions, options)` /
`UseSqlServer<TContext>(connectionString, sqlServerOptions, options)`: the action configures the
Npgsql / SQL Server builder and runs after `options`; `ProviderOptions.Retry`
(`RetryOnFailureOptions`, default 6 retries, 30 s max delay) enables the provider's retrying
strategy; `ProviderOptions.PoolSize` registers a `DbContext` pool instead of a plain context.

With `PoolSize` set, registration throws `ArgumentException` unless the context's only public
constructor takes just `DbContextOptions` or `DbContextOptions<TContext>` (each provider's private `EnsurePoolable`): a
pooled context's constructor-injected scoped services are resolved once from the root provider and
shared by every lease, so a tenant-aware context such as `MultitenantDbContext` must not be pooled.
With `Retry` set, a transaction opened with `BeginTransaction` outside the strategy is rejected.

## Anti-Patterns

```csharp
public sealed class GetUsersHandler(AppDbContext ctx) { }            // ❌ DbContext in a handler — MKP004
await _context.SaveChangesAsync();                                   // ❌ in a repository — MKP001/MKP003
ValueTask<IQueryable<User>> QueryAsync();                            // ❌ IQueryable leak — MKP005
_context.Users.Where(u => u.IsActive).ToListAsync();                 // ❌ raw tracked read — use IReadRepository
.FromSqlRaw($"… WHERE Email = '{email}'");                           // ❌ injection — use FromSqlInterpolated
.IgnoreQueryFilters();                                               // ❌ in library code — IlCallScanTests
```

The module does not rely on lazy loading: what a read needs is loaded by the aggregate load or
`Includes`.
