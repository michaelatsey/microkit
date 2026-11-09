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
  convention fails model building when a non-root entity is inside two roots' boundaries.
- Custom finders call `protected LoadAggregatesAsync(predicate, ct)`, which loads, tracks,
  records and checks the returned roots after the query (identity resolution):

```csharp
var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
return orders.SingleOrDefault();
```

- `AddAsync` and `DeleteAsync` only stage changes; the unit of work commits.

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
a tenant filter the module does not own included. `IlCallScanTests` scans the IL of every library
assembly for it. Only the named overload may be used.

## Unit of Work — `EfUnitOfWork<TContext>`

- `CommitAsync` = `SaveChangesAsync`, translated to `PersistenceException`
  (concurrency conflict, update failure, a provider's transient wrapper).
- `DiscardChanges` = `ChangeTracker.Clear()`, plus a reset of the `AggregateLoadRecord`.
- `ExecuteAsync`: execution strategy + transaction per attempt, retry by unwrapping the
  `DbUpdateException`, join of an open transaction, rollback that never masks, tracker cleared
  after a failed attempt — ADR-006. Do not re-implement any of this in a consumer.
- `AddUnitOfWork<TContext>` registers it scoped as `EfUnitOfWork<TContext>`, `IUnitOfWork`,
  `ITransactionalContext`, `ITransactionalUnitOfWork`, plus `EfDomainEventsProvider<TContext>` as
  `IDomainEventsProvider`.

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
