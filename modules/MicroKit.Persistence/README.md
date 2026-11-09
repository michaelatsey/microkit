# MicroKit.Persistence

Repository, unit-of-work and transaction contracts for DDD aggregates, with an EF Core
implementation for PostgreSQL and SQL Server, MicroKit.Domain type mappings, in-memory test doubles, relational test fixtures
and Roslyn analyzers. Targets .NET 10 and EF Core 10.

Every C# sample below is compiled from
[`samples/MicroKit.Persistence.Samples`](samples/MicroKit.Persistence.Samples), which builds in
Release with the module; `ReadmeSamplesTests` fails if a sample drifts from it.

## Packages

| Package | Contents | Depends on |
|---|---|---|
| `MicroKit.Persistence.Abstractions` | `IRepository<TAggregate, TId>`, `IUnitOfWork`, `ITransactionalContext`, `IPagedResult<T>`, `PersistenceException` | `MicroKit.Domain` |
| `MicroKit.Persistence` | `IReadRepository<TAggregate>`, `QueryOptions<TAggregate>`, pagination, `ISpecificationEvaluator`, the `Result` path (`IResultUnitOfWork`, `PersistenceErrors`) | Abstractions, `MicroKit.Result` |
| `MicroKit.Persistence.EntityFrameworkCore` | EF Core repositories, unit of work, DI registration, Domain conventions | Core, EF Core |
| `MicroKit.Persistence.EntityFrameworkCore.PostgreSql` | `UsePostgreSQL` | EntityFrameworkCore, Npgsql |
| `MicroKit.Persistence.EntityFrameworkCore.SqlServer` | `UseSqlServer` | EntityFrameworkCore, SQL Server provider |
| `MicroKit.Persistence.Specifications` | Ordering and specification-swap extensions for `QueryOptions` | Core |
| `MicroKit.Persistence.Testing` | In-memory repository and unit-of-work doubles; `DockerProbe` | Core |
| `MicroKit.Persistence.Testing.PostgreSql` | `PostgreSqlFixture`: a PostgreSQL container for integration tests | Testing, `Testcontainers.PostgreSql` |
| `MicroKit.Persistence.Testing.SqlServer` | `SqlServerFixture`: a SQL Server container for integration tests | Testing, `Testcontainers.MsSql` |
| `MicroKit.Persistence.Analyzers` | MKP001–MKP009, build-time only | — |

## Composition

Register the provider, the unit of work and the repositories once:

```csharp
services.AddMicroKitPersistence(persistence => persistence
    .AddEntityFrameworkCore(ef => ef
        .UsePostgreSQL<ShopDbContext>(connectionString, options: new ProviderOptions
        {
            Retry = new RetryOnFailureOptions(maxRetryCount: 5),
        })
        .AddUnitOfWork<ShopDbContext>()
        .ConfigurePagination(new PaginationLimits(defaultPageSize: 20, maxPageSize: 100))));

services.AddScoped<IOrderRepository, EfOrderRepository>();
services.AddScoped<IReadRepository<Order>, EfReadRepository<Order, ShopDbContext>>();
return services;
```

`AddUnitOfWork<TContext>()` binds one scoped `EfUnitOfWork<TContext>` to `IUnitOfWork`,
`ITransactionalContext`, `ITransactionalUnitOfWork` and `IResultUnitOfWork`, and registers `EfDomainEventsProvider<TContext>`
as the scoped `IDomainEventsProvider`. `MicroKit.MediatR`'s `TransactionBehavior` consumes them: it
runs each command inside `ITransactionalContext.ExecuteAsync`, dispatches the domain events, then
calls `IUnitOfWork.CommitAsync`.

`ProviderOptions` is off by default:

- `Retry` — a `RetryOnFailureOptions(maxRetryCount = 6, maxRetryDelay = 30 s)` turns on the provider's
  retrying execution strategy. Open transactions through `ITransactionalContext.ExecuteAsync`, which
  runs them inside the execution strategy, never with `Database.BeginTransactionAsync` directly: the
  retrying strategy rejects a transaction opened outside it, and the next query or save on that context
  throws `InvalidOperationException` ([connection
  resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions)).
  Register each context once: EF Core applies every registration's configuration in order, so a second
  `UsePostgreSQL`, `UseSqlServer` or `AddDbContext` for the same context merges into the first (the
  last connection string wins), and a `PoolSize` given after a plain registration does not pool the
  context.
- `PoolSize` — registers the context with `AddDbContextPool`. A pooled context's constructor-injected
  scoped services are resolved once from the root provider and shared by every lease ("scoped services
  injected into the context will only be resolved once from the initial scope", [`AddDbContextPool`
  remarks](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontextpool?view=efcore-10.0)),
  and EF Core does not reject such a context. Tenant-aware contexts such as Tenancy's
  `MultitenantDbContext` (whose constructor also takes an `ITenantContextAccessor`) must not be pooled,
  so with `PoolSize` set, `UsePostgreSQL` and `UseSqlServer` throw `ArgumentException` at registration
  unless the context's only public constructor takes just `DbContextOptions` or
  `DbContextOptions<TContext>`.

## The domain model

Aggregates come from `MicroKit.Domain`. The repositories need no base class beyond `IAggregateRoot`;
an `EntityId<T>` identifier and `AuditableAggregateRoot<TId>` are used here:

```csharp
public sealed record OrderId : EntityId<OrderId>
{
    public OrderId(Guid value) : base(value) { }

    public static OrderId New() => new(Guid.CreateVersion7());
}

public sealed class Order : AuditableAggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<TagLink> _tagLinks = [];

    // Used by EF Core: the audit fields are written through their backing fields.
    private Order(OrderId id, string number) : base(id, DateTimeOffset.UnixEpoch, Actor.Migration) => Number = number;

    private Order(OrderId id, string number, Actor placedBy, TimeProvider clock) : base(id, clock, placedBy) => Number = number;

    public string Number { get; private set; }

    public bool IsClosed { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    // Links to Tag, another aggregate: child entities of the order, loaded and versioned with it.
    public IReadOnlyList<TagLink> TagLinks => _tagLinks;

    public static Order Place(string number, Actor placedBy, TimeProvider clock) =>
        new(OrderId.New(), number, placedBy, clock);

    public void AddLine(string sku, int quantity, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _lines.Add(new OrderLine(Guid.CreateVersion7(), sku, quantity)));

    public void Close(Actor by, TimeProvider clock) => Mutate(clock.GetUtcNow(), by, () => IsClosed = true);

    public void Tag(TagId tag, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _tagLinks.Add(new TagLink(Id, tag)));

    public void Untag(TagId tag, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _tagLinks.RemoveAll(l => l.TagId == tag));
}

public sealed class OrderLine(Guid id, string sku, int quantity)
{
    public Guid Id { get; private set; } = id;

    public string Sku { get; private set; } = sku;

    public int Quantity { get; private set; } = quantity;
}

public sealed class TagLink(OrderId orderId, TagId tagId)
{
    public OrderId OrderId { get; private set; } = orderId;

    public TagId TagId { get; private set; } = tagId;
}

public sealed record TagId : EntityId<TagId>
{
    public TagId(Guid value) : base(value) { }
}

public sealed class Tag(TagId id, string name) : AggregateRoot<TagId>(id)
{
    public string Name { get; private set; } = name;
}
```

## The DbContext

`AddDomainConventions` maps the `MicroKit.Domain` types without hand-written converters:

```csharp
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddDomainConventions(typeof(OrderId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.Ignore(o => o.DomainEvents);
            order.HasIndex(o => o.Number).IsUnique(); // a second SO-1: PERSISTENCE.ORDER.DUPLICATE
            order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId");
            order.HasMany(o => o.TagLinks).WithOne().HasForeignKey(l => l.OrderId);
        });

        modelBuilder.Entity<TagLink>(link =>
        {
            link.HasKey(l => new { l.OrderId, l.TagId });
            link.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId); // another aggregate: Restrict by convention
        });

        modelBuilder.Entity<Tag>().Ignore(t => t.DomainEvents);

        // The domain assigns the line's Guid key: without this, EF Core treats a Guid key as
        // generated and tracks a new line with a key already set as Modified, not Added.
        modelBuilder.Entity<OrderLine>().Property(l => l.Id).ValueGeneratedNever();
    }
}
```

| Convention | Maps |
|---|---|
| `AddEntityIdConventions(assemblies)` | every `record X : EntityId<X>` of the assemblies, and `GuidId`, to a `Guid` column, read back through the `(Guid)` constructor whatever its accessibility; model building fails, naming the type, when there is none |
| `AddActorConvention()` | `Actor` to a string of at most `Actor.MaxLength` (200), read back through `Actor.Restore` |
| `AddAuditInstantConvention()` | `CreatedAt` / `UpdatedAt` of every `IAuditableEntity` normalised to offset zero; a stored `0001-01-01` is rejected |

A key whose CLR type is `Guid` is generated by EF Core by default. A child entity whose `Guid` key
the domain assigns, such as `OrderLine` here, needs `ValueGeneratedNever()`: otherwise a new child
with its key already set is tracked as `Modified` and updated, not inserted ([EF Core 3.0 breaking
changes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-3.x/breaking-changes#detectchanges-honors-store-generated-key-values)).
`EntityId<T>` keys need nothing: an identifier type is never generated.

A conversion configured explicitly in `OnModelCreating` wins. Compiled models
(`dotnet ef dbcontext optimize`) and NativeAOT precompiled queries are not supported by these
conventions.

## The write side

`IRepository<TAggregate, TId>` is the only write-side contract: `FindAsync` (optionally at an expected
version), `AddAsync`, `DeleteAsync` and `GetVersion` (see [Optimistic concurrency](#optimistic-concurrency)).
It never commits — `IUnitOfWork` does — and has no update method: an aggregate it returns is tracked.

```csharp
public interface IOrderRepository : IRepository<Order, OrderId>
{
    ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default);
}

public sealed class EfOrderRepository(ShopDbContext context)
    : EfRepository<Order, OrderId, ShopDbContext>(context), IOrderRepository
{
    public async ValueTask<Order?> FindByNumberAsync(string number, CancellationToken ct = default)
    {
        var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
        return orders.SingleOrDefault();
    }
}
```

`EfRepository.FindAsync(id)`, and `LoadAggregatesAsync(predicate)` for a typed finder:

- load the **whole aggregate**, tracked, as the model's boundary defines it (below); owned entities
  come with it. A finder states which roots it wants, never what is loaded with them: there is no
  include to add and no query to shape;
- return a root a repository over the same context already loaded whole or staged with `AddAsync`
  (committed or not), or any `Added` root, as is — `FindAsync` without a query;
- **refuse** any other root the context tracks, with an `InvalidOperationException` naming its type:
  a root tracked by a direct `DbSet` query, a filtered `Include`, an explicit load, an `Attach` or
  `Update`, or a reattachment after `ChangeTracker.Clear()` may be partial, and a partial aggregate
  must never reach a command;
- split the load (`AsSplitQuery`) when the aggregate has two or more collections. A split load runs one
  statement per collection: under Read Committed they can see different snapshots. The aggregate's
  version (see [Optimistic concurrency](#optimistic-concurrency)) makes such a torn read fail the
  commit when the command writes; a read-only decision across root and children needs Snapshot or
  Serializable isolation.

### The aggregate boundary

The boundary lives in the model, and only there. By default it is every navigation to an entity that
does not implement `IAggregateRoot`, recursively — derived types included; another aggregate root is
never part of it. Mark a navigation to reference data `IsOutsideAggregate()`: it is
neither loaded with the aggregate nor counted as part of it.

```csharp
modelBuilder.Entity<CatalogItem>(item =>
{
    item.Ignore(i => i.DomainEvents);
    item.HasOne(i => i.Origin).WithMany().HasForeignKey(i => i.OriginCode);
    item.Navigation(i => i.Origin).IsOutsideAggregate(); // reference data: never loaded with the item
});
```

Model building fails, naming the types, when an entity that is not an aggregate root lies inside the
boundary of two roots — directly, or through a base type and one of its derived types. Each ownership
of an owned type is its own entity type, so the same owned type under two roots is not shared. Two
derived types that declare same-named navigations to different types fail at model building too.

Uniqueness holds per row too: a navigation brings an entity into the boundary only if each of its rows
has one parent row — a collection or a reference whose child holds the foreign key, or a one-to-one
whose parent holds a unique foreign key. A many-to-one reference held by the parent that reaches a
non-root entity fails model building, naming the navigation: make the target an aggregate root, or mark
the navigation `IsOutsideAggregate()`.

A many-to-many (skip) navigation declared inside a boundary fails model building whatever its target,
unless it is marked `IsOutsideAggregate()` — then it is neither loaded nor versioned, and its join rows
lie outside every aggregate. Links an aggregate owns are an explicit link entity, a child of the root
that references the other root — the `TagLinks` of the `Order` above, keyed by `(OrderId, TagId)`:

```csharp
modelBuilder.Entity<TagLink>(link =>
{
    link.HasKey(l => new { l.OrderId, l.TagId });
    link.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId); // another aggregate: Restrict by convention
});
```

The links are loaded, versioned and removable like any child, and the `Tag` itself is not tracked:
it loads whole through its own repository, in the same context if needed.

The database never changes an aggregate on behalf of something outside it. A foreign key from inside
an aggregate to another root, or to an entity outside every aggregate — `TagLink.TagId` above, an
`Order.CustomerId` — gets `DeleteBehavior.Restrict`, instead of EF Core's `Cascade` default for a
required relationship ([cascade
delete](https://learn.microsoft.com/ef/core/saving/cascade-delete#configuring-cascading-behaviors)):
deleting a `Tag` that is still linked fails, and nothing changes. An explicit `Cascade` or `SetNull`
on such a key fails model building, naming it. Relationships inside one boundary keep their
behaviour: a child still cascades with its root. On an existing database **a migration is needed**: those
constraints change from `CASCADE` to `RESTRICT` (`NO ACTION` on SQL Server). A key into a soft-deletable
aggregate is `ClientNoAction` instead, and soft-deleting its root does not fail — see
[Soft delete](#soft-delete).

The boundary validation and the record of the roots a repository loaded whole come with
`UseMicroKitPersistence()`. `AddDbContext`, `UsePostgreSQL` and `UseSqlServer` call it; a context
configured another way calls it itself, or `EfRepository` refuses to load:

```csharp
new DbContextOptionsBuilder<ShopDbContext>()
    .UseNpgsql(connectionString)
    .UseMicroKitPersistence()
    .Options;
```

A root is recorded once the load that returned it has completed — a load that fails midway records
nothing, and a root the model auto-includes without its own aggregate is never recorded. The record
follows the change tracker: `IUnitOfWork.DiscardChanges()` and a failed `ExecuteAsync` attempt reset
it, and a pooled context resets it when it returns to the pool. An owned navigation cannot be marked
`IsOutsideAggregate()`: an owned entity is always loaded with its owner.

## Optimistic concurrency

Every aggregate root carries a version: a shadow `long` concurrency token named
`AggregateVersion` (`AggregateVersion.PropertyName`), added by `UseMicroKitPersistence()`. Every
save that writes any entity of an aggregate — the root, a child, a grand-child, an owned type, a
link entity — bumps its root's version by one, so two commands that change different
children of the same aggregate conflict: the second save fails with a `PersistenceException`
wrapping `DbUpdateConcurrencyException`, and nothing of it is written. The check runs in the root's
`UPDATE` (`WHERE AggregateVersion = @original`).

On an existing database **a migration is needed**: it adds a `NOT NULL` column to every root's table (EF Core
scaffolds `defaultValue: 0L`), so existing rows start at 0 and their first change takes them to 1.

- An inserted aggregate starts at 1; `GetVersion` reads 0 on an `Added` root until its commit, and a failed save
  puts the version back.
- A child that moves from one aggregate to another bumps both, and both must have been loaded or
  staged by their repository; a changed child whose root is not tracked fails the save. Entities
  outside every aggregate (an outbox row, reference data) are ignored.
- The property name is reserved on aggregate roots. Only its column may be configured
  (`Property<long>(AggregateVersion.PropertyName).HasColumnName("row_version")`), and a naming
  convention applies to it. Model building fails when a root maps the name to a CLR member, gives it a
  type other than `long`, stops it being a concurrency token, maps another property to its column, or
  gives it a default or computed value, value generation, a value converter or a provider type. The
  column check covers every table that stores the column — each concrete type's table under TPC — and
  only the entity types mapped to it.

Across requests — an HTTP `ETag` / `If-Match` — read the version from a loaded aggregate or from a
projection, and state it on the command:

```csharp
public async ValueTask<long> HandleAsync(OrderId id, long ifMatch, string sku, Actor by, CancellationToken ct = default)
{
    var order = await orders.FindAsync(id, expectedVersion: ifMatch, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Order {id} does not exist.");

    order.AddLine(sku, 1, by, clock);
    await unitOfWork.CommitAsync(ct).ConfigureAwait(false); // UPDATE … WHERE AggregateVersion = @ifMatch

    return orders.GetVersion(order); // the new ETag
}
```

```csharp
public static IQueryable<OrderSummary> Of(ShopDbContext context) =>
    context.Orders.AsNoTracking()
        .Select(o => new OrderSummary(o.Id, o.Number, EF.Property<long>(o, AggregateVersion.PropertyName)));
```

The expected version becomes the token's original value: the check runs in the `UPDATE` of the
commit, never as a read-then-compare. A command that states the current version but changes
nothing writes nothing. A command that states a stale version fails with a conflict even when it
changes nothing: EF Core sees the original value differ from the loaded one and writes the root's
token, `WHERE AggregateVersion = @stale`, which matches no row.

A staged aggregate not inserted yet has version 0: `FindAsync(id, expectedVersion)` checks it at the
find, and any other expected value throws a `PersistenceException` whose inner exception is an
`AggregateVersionConflictException`. A delete is checked too: `DeleteAsync` then a commit fails
when another command changed the aggregate since it was loaded.

Known limits:

- Bulk writes bypass the version: `ExecuteUpdate`, `ExecuteDelete` and raw SQL do not go through the
  change tracker, so they write an aggregate's rows without bumping its version and without
  conflicting with a concurrent command.
- MicroKit bumps the versions in a `SaveChanges` interceptor that runs before the interceptors the
  application adds with `AddInterceptors`: a change those make to an aggregate during `SavingChanges`
  is written without a bump.
- A many-to-many navigation marked `IsOutsideAggregate()` is not versioned: its join rows lie outside
  every aggregate. Use a link entity for links the aggregate owns.

## Soft delete

An aggregate opts in on its root; deleting the root then marks the whole aggregate instead of deleting
it. It needs `UseMicroKitPersistence()` on the context (`AddDbContext`, `UsePostgreSQL` and
`UseSqlServer` call it): without it the annotation does nothing and a delete is real.

```csharp
        // README: "Soft delete"
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Ignore(i => i.DomainEvents);
            invoice.HasSoftDelete(); // the invoice and its lines are marked, never deleted
            invoice.HasMany(i => i.Lines).WithOne().HasForeignKey("InvoiceId").IsRequired(); // Cascade inside the aggregate

            // A soft-deleted invoice still holds its number: filter the unique index on the live rows.
            invoice.HasIndex(i => i.Number).IsUnique().HasFilter("\"AggregateDeletedAt\" IS NULL");
        });
```

- **The model.** Every entity type of the aggregate gets a shadow `DateTimeOffset?` column,
  `AggregateDeletedAt` (`SoftDelete.PropertyName`), and the named query filter `MicroKit.SoftDelete`
  (`SoftDelete.FilterName`), on the root type of each hierarchy; an owned type follows its owner. The
  property name is reserved on those types; its column may be configured, and a naming convention
  applies. Model building fails when one of them maps the name to a CLR member, gives it a type other than
  `DateTimeOffset?`, maps another property of its table to its column, or configures it beyond its column
  (required, a default or computed value, value generation, a converter); the column check covers every
  table that stores it, each concrete type's under TPC. It is a schema change: a migration adds a nullable
  column to each table of the aggregate.
- **The save.** `DeleteAsync(root)` then a commit writes an `UPDATE` that sets the mark — one UTC instant
  per save, from the application's `TimeProvider` (`TimeProvider.System` if none is registered, or when the
  context has no application service provider — options built by hand without `UseApplicationServiceProvider`;
  a pooled context resolves it from the root provider, so a scoped `TimeProvider` is not seen) — on the root and on
  every tracked entity of its aggregate, whatever EF Core's cascade timing or delete behaviour left it
  as; no row is deleted, and owned values are kept. A change made before the delete in the same save is
  written with the mark. The root's version is bumped and checked like any write. After the commit the
  aggregate is detached, as a deleted one is, and `FindAsync` no longer finds it — once the save accepts
  its changes: after `SaveChanges(acceptAllChangesOnSuccess: false)` it stays tracked, so a save retried
  after a rollback writes the mark again; after `AcceptAllChanges()` the repository no longer returns it.
- **What stays a real delete.** A child removed from an aggregate that stays alive; an aggregate without
  `HasSoftDelete()`; bulk `ExecuteDelete` and raw SQL, which bypass the change tracker.
- **References from outside.** A foreign key into the aggregate from outside it (another aggregate, an
  entity outside every aggregate, join rows of an outside many-to-many) is `ClientNoAction`: EF Core leaves
  a tracked dependent alone when the root is removed, and the database constraint is `NO ACTION`. A real
  delete of a referenced child still fails, and `TryExecuteAsync` returns `REFERENCE`. Configuring another
  behaviour on such a key fails model building. **A migration is needed on PostgreSQL**: those constraints
  change from `RESTRICT` (the cross-aggregate default above) to `NO ACTION`; SQL Server keeps `NO ACTION`.
- **Refusals.** Deleting a root its repository did not load whole fails the save: its untracked entities
  could not be marked. Model building fails when an entity of the aggregate carries an anonymous query
  filter (name it: `HasQueryFilter("Tenant", …)`, so both filters combine), and when a foreign key
  between two entity types of the aggregate is required and does not cascade (`Restrict`, `NoAction`,
  `ClientSetNull`, `SetNull`), whether or not a navigation reaches it, inherited keys and keys to a mapped
  base type of the root included: EF Core would reject the removal of the root before the save could mark
  it. `ClientNoAction` is accepted; its child is marked. `HasSoftDelete()` on a type that is not a top-most
  aggregate root fails model building too.
- **Reads.** The filter hides soft-deleted rows from every query, a direct query on a child's `DbSet`
  included. `QueryOptions.WithDeleted()` returns them on the read side by ignoring that filter only
  (`IgnoreQueryFilters([SoftDelete.FilterName])`), for the whole query — an `Include` that reaches another
  soft-deletable aggregate returns its soft-deleted rows too. Every other filter, named or anonymous (a
  tenant filter), still applies: to see rows an application filter of its own hides, ignore that filter by
  name in a typed read repository.
- **Tenancy.** `MicroKit.Tenancy`'s tenant filter is anonymous today: a tenant entity cannot be part of a
  soft-deletable aggregate until that filter is named (model building fails, naming the entity).

```csharp
    // README: "Soft delete" — the read side, with the soft-deleted invoices too
    public ValueTask<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct = default) =>
        ListAsync(new QueryOptions<Invoice>().WithDeleted().WithOrderBy(i => i.Number), ct);
```

Known limits:

- A unique index still sees soft-deleted rows: a soft-deleted invoice keeps its number. Filter the index
  on the live rows, as above (`HasFilter("\"AggregateDeletedAt\" IS NULL")`, valid on PostgreSQL and on
  SQL Server, whose filtered indexes run with `QUOTED_IDENTIFIER ON`; with a naming convention, name the
  converted column). No convention rewrites the application's indexes.
- A required navigation from another aggregate to a soft-deleted root: EF Core warns that the filter can
  hide the principal of a required relationship (`PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning`),
  and an `Include` of it drops the dependent rows.
- There is no undelete: the write side never loads a soft-deleted aggregate.
- The in-memory doubles delete for real; `WithDeleted()` has no effect on them.

## Transactions

`ITransactionalContext.ExecuteAsync` runs an operation in a transaction:

- with no transaction open, it begins one inside the provider's execution strategy, commits on success
  and rolls back on failure. A transient failure of the commit is retried — the whole operation runs
  again, so it must be idempotent, and it must load what it modifies (the change tracker is cleared after
  a failed attempt);
- with a transaction already open — an enclosing `ExecuteAsync`, or one the caller opened — it joins it:
  no strategy, no begin, commit or rollback;
- the rollback never runs with a cancelled token and never replaces the original exception;
- the outermost call surfaces every database failure as a `PersistenceException`; a joined call lets
  the operation's exceptions through to the transaction's owner;
- an ambient `System.Transactions` transaction (`TransactionScope`) is rejected, as EF Core rejects it:
  with retry on, the execution strategy refuses to start; without, beginning the transaction fails. A
  distributed transaction is not a consistency mechanism between services: they go through the outbox.

### A command without MediatR

```csharp
transactions.ExecuteAsync(
    static async (s, token) =>
    {
        var order = await s.Orders.FindAsync(s.Id, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Order {s.Id} does not exist.");

        order.AddLine(s.Sku, s.Quantity, s.By, s.Clock);
        await s.UnitOfWork.CommitAsync(token).ConfigureAwait(false);
    },
    (Orders: orders, UnitOfWork: unitOfWork, Clock: clock, Id: id, Sku: sku, Quantity: quantity, By: by),
    ct);
```

## Persistence failures as `Result`

`IResultUnitOfWork` (core) returns three persistence failures as `MicroKit.Result` errors of category
`Conflict` instead of throwing; every other failure still throws `PersistenceException`:

| Failure | Recognised by | Code |
|---|---|---|
| Unique-constraint violation | PostgreSQL `23505`, SQL Server `2627`/`2601` | `PERSISTENCE.<ROOT>.DUPLICATE` |
| Foreign-key violation | PostgreSQL `23503`, SQL Server `547` (see below) | `PERSISTENCE.<ROOT>.REFERENCE` |
| Concurrency conflict | `DbUpdateConcurrencyException`, a stale expected version | `PERSISTENCE.<ROOT>.CONCURRENCY` |

```csharp
public sealed class PlaceOrder(
    IResultUnitOfWork transactions,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // README: "Persistence failures as Result"
    public ValueTask<Result<OrderId>> HandleAsync(string number, Actor by, CancellationToken ct = default) =>
        transactions.TryExecuteAsync(
            static async (s, token) =>
            {
                if (string.IsNullOrWhiteSpace(s.Number))
                    return Result.Failure<OrderId>(Error.Validation(ErrorCode.From("ORDER.NUMBER.EMPTY"), "An order needs a number."));

                var order = Order.Place(s.Number, s.By, s.Clock);
                await s.Orders.AddAsync(order, token).ConfigureAwait(false);
                await s.UnitOfWork.CommitAsync(token).ConfigureAwait(false);
                return Result.Success(order.Id);
            },
            (Orders: orders, UnitOfWork: unitOfWork, Clock: clock, Number: number, By: by),
            ct);
}
```

A second `SO-1` returns `PERSISTENCE.ORDER.DUPLICATE` (409 through `MicroKit.Result.AspNetCore`), and
nothing of the command is written.

- **Where it converts.** `TryExecuteAsync` runs the operation as `ExecuteAsync` does — transaction,
  execution strategy, transient failures retried — and converts a failure only once the transaction it
  owns is rolled back and the strategy has given up. Converting inside the transaction would let the
  operation return normally, and the owner would commit what was written before it (an earlier flush,
  an `ExecuteUpdate`, outbox rows). Joined to an open transaction, it converts nothing: the exception
  reaches the owner, which rolls back.
- **A failed `Result` the operation returns** is returned unchanged, and the transaction it owns is
  rolled back instead of committed: a failed command persists none of its work. Joined, the owner
  decides.
- **`TryCommitAsync`** is the `Result` counterpart of `CommitAsync`, for code with no open transaction
  (a flush is atomic by itself); inside an open transaction it throws `InvalidOperationException`.
- **`<ROOT>`** is the failing entity's aggregate root, by default its simple CLR name in UPPER_SNAKE
  (`CatalogItem` → `CATALOG_ITEM`; a failing `OrderLine` reports its root, `ORDER`);
  `Entity<Billing.Order>().HasAggregateErrorName("BILLING_ORDER")` fixes it. Model building fails when two roots
  resolve to the same name, when `HasAggregateErrorName` is set on a type that is not a top-most root, and — on every
  context with `UseMicroKitPersistence()`, whether the `Result` path is used or not — when a root's default name is
  not valid UPPER_SNAKE (a CLR name such as `Opération`, or one starting with `_`): give it a name. When the failing entities belong
  to more than one aggregate, or to none (an outbox row, a constraint checked at COMMIT), the code is
  `PERSISTENCE.<REASON>`: a root is never guessed.
- **The message is fixed and client-safe**: no table, constraint or column name. The provider exception
  stays in process, in EF Core's log (`SaveChangesFailed`).
- **Provider codes** are recognised by `PostgreSqlFailureClassifier` and `SqlServerFailureClassifier`,
  which `UsePostgreSQL` and `UseSqlServer` configure. A context configured another way passes one:
  `UseMicroKitPersistence(PostgreSqlFailureClassifier.Instance)` — `AddDbContext` registers none, so pass it
  in its `configure` delegate; without one, only concurrency conflicts are converted.
- **SQL Server `547`** is reported for a foreign-key and for a check violation alike. It is decided from
  the model: `REFERENCE` only when no entity type mapped to the failing entries' tables declares a check
  constraint (`HasCheckConstraint`); otherwise it is not converted. A check constraint that exists in the
  database but not in the model cannot be seen, and its violation is reported as `REFERENCE`. A
  PostgreSQL check violation (`23514`) is never converted.
- A strategy that retries every exception, rather than the provider's transient ones, would also re-run
  an operation that returned a failed `Result`.
- `IResultUnitOfWork` writes nothing by itself: the operation flushes with `IUnitOfWork.CommitAsync`, so
  a handler injects both. After a converted `TryCommitAsync` failure the change set is still pending;
  call `DiscardChanges()` before reusing the scope.
- Inside a transaction someone else owns — MicroKit.MediatR's `TransactionBehavior` runs every command
  in `ExecuteAsync` — `TryExecuteAsync` is joined and converts nothing, and `TryCommitAsync` is refused:
  the conversion belongs to the transaction's owner. MicroKit.MediatR adopts it during its own upgrade.

The in-memory double follows the same transaction rules: a version conflict of `InMemoryRepository`
(checked at the find) becomes `PERSISTENCE.<ROOT>.CONCURRENCY` with the default name inside
`TryExecuteAsync`, and a failed operation counts no commit:

```csharp
var orders = new InMemoryRepository<Order, OrderId>();
var unitOfWork = new InMemoryUnitOfWork();
var order = Order.Place("SO-1", new Actor("user-1"), TimeProvider.System);
await orders.AddAsync(order);
orders.SetVersion(order, 2); // another command changed it

var result = await unitOfWork.TryExecuteAsync(
    static async (s, ct) =>
    {
        var stale = await s.Orders.FindAsync(s.Id, expectedVersion: 1, ct);
        stale!.AddLine("BOOK", 1, new Actor("user-1"), TimeProvider.System);
        await s.UnitOfWork.CommitAsync(ct);
        return Result.Success();
    },
    (Orders: orders, UnitOfWork: unitOfWork, order.Id));

// result.Error.Code == "PERSISTENCE.ORDER.CONCURRENCY", category Conflict; no commit counted.
```

The double cannot roll its store back: a failed operation's writes stay visible in it. It has no model,
so a name fixed with `HasAggregateErrorName` is unknown to it. It has no constraints either: it never
returns `DUPLICATE` or `REFERENCE` (adding an aggregate twice throws at `AddAsync`), and since it checks
versions at the find, a stale find outside `TryExecuteAsync` throws — a `PersistenceException` whose inner
exception is an `AggregateVersionConflictException` — instead of reaching `TryCommitAsync` as a `CONCURRENCY`
error.

## The read side

`IReadRepository<TAggregate>` queries through `QueryOptions<TAggregate>`, which pairs a domain
`Specification<T>` (what) with includes, split query, ordering and pagination (how). The read side
never tracks: there is no tracking option, and every method returns new, untracked instances.

```csharp
public sealed class OpenOrdersSpec : Specification<Order>
{
    public override System.Linq.Expressions.Expression<Func<Order, bool>> ToExpression() => order => !order.IsClosed;
}

public sealed class ListOpenOrders(IReadRepository<Order> orders)
{
    public ValueTask<IPagedResult<Order>> HandleAsync(int page, CancellationToken ct = default) =>
        orders.ListPagedAsync(
            new QueryOptions<Order>(new OpenOrdersSpec())
                .WithOrderByDescending(o => o.CreatedAt)
                .WithThenBy(o => o.Id) // a unique tie-breaker: stable page boundaries
                .WithPagination(page, pageSize: 20),
            ct);
}
```

- `ListPagedAsync` without pagination returns the first page of `PaginationLimits.DefaultPageSize`
  items (50 unless configured), never the whole table; a page larger than `MaxPageSize` (500) is
  rejected. `ListAsync` without pagination returns every match.
- `FindAsync(keyValues)` and the list methods never consult the change tracker: an aggregate the
  command tracks in the same context comes back as a new, untracked instance, and its pending change
  is not visible through it. Load what a command modifies through `IRepository<TAggregate, TId>`. Each key
  value must have its key property's CLR type, or `FindAsync` throws `ArgumentException`, as `DbSet.Find` does.
- Ordering is required for stable pages.

### Projections

A query handler usually returns a DTO, not the aggregate. The projection overloads of `ListAsync` and
`ListPagedAsync` take a selector and let the database build it: the specification, the ordering and the
pagination apply, and `AsSplitQuery()` splits the collections the selector projects; the includes do not
apply, since the selector says what is read.

```csharp
public sealed record OrderListItem(OrderId Id, string Number, int Lines);

public sealed class ListOpenOrderSummaries(IReadRepository<Order> orders)
{
    public ValueTask<IReadOnlyList<OrderListItem>> HandleAsync(CancellationToken ct = default) =>
        orders.ListAsync(
            new QueryOptions<Order>(new OpenOrdersSpec()).WithOrderBy(o => o.Number),
            o => new OrderListItem(o.Id, o.Number, o.Lines.Count), // built by the database
            ct);
}
```

### Keyset pagination

An offset page costs more the deeper it is; a keyset page starts after the last row of the page before,
by comparing its sort keys in the database, and costs the same at any depth. Set the sort keys with
`KeysetPagination<T>` and read the page with `ListKeysetAsync` (with or without a projection):

```csharp
public sealed record OrderListPage(IReadOnlyList<OrderListItem> Items, string? Next);

public sealed class BrowseOpenOrders(IReadRepository<Order> orders)
{
    // null: the token is malformed, and the HTTP layer answers 400.
    public async ValueTask<OrderListPage?> HandleAsync(string? after, CancellationToken ct = default)
    {
        KeysetCursor? cursor = null;
        if (after is not null && !KeysetCursor.TryParse(after, out cursor))
            return null;

        var opts = new QueryOptions<Order>(new OpenOrdersSpec())
            .WithKeyset(KeysetPagination<Order>.OrderByDescending(o => o.CreatedAt) with { PageSize = 20, After = cursor });
        var page = await orders.ListKeysetAsync(opts, o => new OrderListItem(o.Id, o.Number, o.Lines.Count), ct).ConfigureAwait(false);
        return new OrderListPage(page.Items, page.Next?.ToString());
    }
}
```

- **The ordering is total.** The root's primary key is appended, ascending, as the last sort key: rows
  that tie on the sort keys are neither skipped nor repeated across pages.
- **Sort keys** are scalar properties of the root (read directly or through an interface it implements), not
  nullable and not a primitive collection: a `Nullable<T>` key is refused when the keyset is built, a nullable
  column when the page is read. An index on the sort keys plus the primary key serves the page: its query
  starts with a range on the first key. Every key is stored as a type the cursor
  carries (integers, `Guid`, `string`, `bool`, `decimal`, `double`, `float`, `DateTime`, `DateTimeOffset`,
  `DateOnly`, `TimeOnly`, `byte[]`), after its value converter: a strongly typed id stored as a `Guid` is
  fine. Another stored type fails, naming the property.
- **Forward only.** Each page returns `Next`, the cursor of the next one, `null` on the last page. There is
  no total count; use `CountAsync` if one is needed.
- **The cursor is an opaque token.** `ToString()` gives it to a client; `KeysetCursor.TryParse` reads it
  back and checks its format only (it is untrusted input, bounded by `KeysetCursor.MaxTokenLength`). The
  page checks the rest: a cursor issued for another aggregate type or another ordering, or whose values do
  not match the sort keys, throws `ArgumentException`. The token is not encrypted: do not sort by a secret.
- The keyset owns the ordering and the page: options that also set an ordering or an offset pagination are
  refused, and `ListAsync`, `ListPagedAsync`, `AnyAsync` and `CountAsync` refuse options with a keyset. `PaginationLimits` bounds the
  page size, and gives the default.

### Compiled finds

`IRepository.FindAsync(id)` (the whole-aggregate load) and `IReadRepository.FindAsync(keyValues)` for a
single-property key run queries compiled once per model. A query filter that reads context state — a
tenant — is still evaluated per context, at each execution.

## Testing without a database

```csharp
var orders = new InMemoryRepository<Order, OrderId>();
var unitOfWork = new InMemoryUnitOfWork();
var clock = TimeProvider.System;
var order = Order.Place("SO-1", new Actor("user-1"), clock);

await orders.AddAsync(order);
(await orders.FindAsync(order.Id))!.AddLine("BOOK", 1, new Actor("user-1"), clock);
await unitOfWork.CommitAsync();
```

`InMemoryRepository<TAggregate, TId>` implements both repository contracts on one store, and
`InMemoryReadRepository<TAggregate>` the read one; both enforce the same pagination limits as the EF Core
repository and support projections and keyset pages. Projections compile the selector; keyset pages order in
memory (strings ordinally) with the store's key appended, and their cursors are valid only on the double
that issued them. Its versions start at 1 and do not move on their
own (it cannot see mutations): `SetVersion` sets one, and `FindAsync(id, expectedVersion)` checks it at
the find, where EF Core checks it at the commit.

## Testing against a real database

`MicroKit.Persistence.Testing.PostgreSql` and `MicroKit.Persistence.Testing.SqlServer` each ship one fixture, a
database server in a Docker container (Testcontainers), with an `IAsyncDisposable` lifecycle and no test framework:

```csharp
if (!DockerProbe.IsAvailable)
    return false; // in a test: skip, the test framework's way

await using var postgres = new PostgreSqlFixture();
await postgres.StartAsync();

// One database per test, on the shared server.
var connectionString = postgres.GetConnectionString("orders_" + Guid.NewGuid().ToString("N"));
```

- **`DockerProbe.IsAvailable`** (in `MicroKit.Persistence.Testing`) tells whether a Docker daemon answers, so a test
  can skip rather than fail. It never throws; it sends `GET /_ping` and counts only an HTTP 200, so a socket file
  a stopped daemon left behind reads as no daemon. The first read probes `DOCKER_HOST` when set, otherwise
  `/var/run/docker.sock`, `$XDG_RUNTIME_DIR/docker.sock` and the Windows pipe, up to about 4 seconds per source
  (a 2-second connect, then a 2-second deadline on the whole ping);
  the answer is then cached. It does not read what only Testcontainers reads (`~/.testcontainers.properties`,
  Docker contexts, Docker Desktop's own socket): where only those reach the daemon, the probe says no and the test
  is skipped although it could run. The fixtures never consult it, so the probe can skip a test but never fail one.
- **`StartAsync`** starts one container, whoever calls it and however often; a caller's token cancels only its own
  wait. A start that fails is rethrown to every later call; once the fixture is disposed every call throws
  `ObjectDisposedException`. The first start may pull the image. Without a daemon it fails with Testcontainers' own
  exception.
- **`ConnectionString`** is the server's default database (`postgres`, `master`); **`GetConnectionString(database)`**
  is the same server with another database, which `EnsureCreated` or a migration then creates. Both throw before a
  successful start.
- **`DisposeAsync`** cancels a running start, then stops and removes the container; every call, concurrent ones
  included, completes once it is removed. It is safe twice or on a fixture that never started.
- The images default to `PostgreSqlFixture.DefaultImage` (`postgres:17-alpine`) and `SqlServerFixture.DefaultImage`
  (`mcr.microsoft.com/mssql/server:2022-latest`); pass the image production runs to the constructor. Testcontainers'
  SQL Server builder sets `ACCEPT_EULA=Y`: using `SqlServerFixture` accepts the SQL Server licence. Its image is
  amd64 and runs emulated on ARM.

Hook the fixture into the framework's lifecycle and share it across tests — xUnit's `IAsyncLifetime` on a class or
collection fixture, NUnit's `[OneTimeSetUp]`/`[OneTimeTearDown]`, MSTest's `[ClassInitialize]`/`[ClassCleanup]`.
The module's own suites run on these fixtures through a ten-line xUnit adapter,
[`PostgreSqlSuiteFixture`](tests/MicroKit.Persistence.IntegrationTests/PostgreSql/PostgreSqlSuiteFixture.cs).

## Analyzers

| ID | Severity | Category | Rule |
|---|---|---|---|
| MKP001 | Error | Usage | A read repository calls `CommitAsync` or `SaveChangesAsync` |
| MKP002 | Error | Usage | A read repository declares or calls a write method |
| MKP003 | Warning | Usage | `SaveChanges` called directly on a `DbContext`, bypassing `IUnitOfWork` |
| MKP004 | Warning | Design | A `DbContext` injected outside the infrastructure layer |
| MKP005 | Error | Design | A repository method returns `IQueryable<T>` |
| MKP006 | Info | Performance | One query includes two or more collections without `AsSplitQuery()` or `AsSingleQuery()` (on `QueryOptions`, the options' `AsSplitQuery()` or `AsSplitQueryEnabled = true` settles it) |
| MKP007 | Warning | Performance | A query runs on each iteration of a `foreach` or `for` loop: a LINQ executor, `Find`, explicit loading, a repository read |
| MKP008 | Warning | Performance | A query from a `DbSet` is materialized (`ToList`, `ToArray`, their async forms), then filtered in memory by a predicate that translates |
| MKP009 | Warning | Usage | The parameterless `IgnoreQueryFilters()`, which disables every filter, tenant filters included; name the filters, or use `WithDeleted()` |

MKP006–MKP009 check query shapes. What they cannot see they do not guess — MKP006 and MKP008 judge a query chain in
one expression — and they ship no code fix: no rewrite of these shapes keeps the query's results (ADR-014).
MKP003, MKP004 and MKP007–MKP009 are warnings: a build with `TreatWarningsAsErrors` reports them as errors. The
categories are `MicroKit.Persistence.Usage`, `.Design` and `.Performance`;
`dotnet_analyzer_diagnostic.category-MicroKit.Persistence.Performance.severity` sets a whole category. Where a team
means a shape, `.editorconfig` sets the severity — a model that configures splitting globally, or a loop over a
handful of keys — and `#pragma warning disable MKP007` suppresses one site:

```ini
[*.cs]
dotnet_diagnostic.MKP006.severity = none      # UseQuerySplittingBehavior(SplitQuery) is configured
dotnet_diagnostic.MKP007.severity = suggestion
```

`while`, `do` and condition-less `for` loops are not reported (the batching and retry idioms), nor is a page walk
with `ListKeysetAsync` or `ListPagedAsync`. MKP006 is silent when EF Core ignores the includes (a projection, a count). MKP009 stays a warning, not an error: an anonymous query filter can only be ignored by the
parameterless overload.

Each rule has its own section below; the analyzers' help links open it.

### MKP001

**Error — a read repository calls `CommitAsync` or `SaveChangesAsync`.** A type implementing
`IReadRepository<T>` calls `CommitAsync`, `SaveChangesAsync` or `SaveChanges`. The read side never commits: a
query handler that writes breaks the CQRS split, and its write escapes the unit of work. Move the write to a
command handler, through `IRepository<TAggregate, TId>` and `IUnitOfWork`.

### MKP002

**Error — a read repository declares or calls a write method.** A type implementing `IReadRepository<T>`
declares or calls `AddAsync`, `UpdateAsync` or `DeleteAsync`. Writes belong to `IRepository<TAggregate, TId>`;
keep the read repository to finds, lists, pages, `AnyAsync` and `CountAsync`.

### MKP003

**Warning — `SaveChanges` called directly on a `DbContext`.** A call to `SaveChanges` or `SaveChangesAsync` on a
`DbContext`, outside a type implementing `IUnitOfWork`, bypasses the unit of work and its error translation.
Commit through `IUnitOfWork.CommitAsync`. A `DbContext` that overrides `SaveChangesAsync` is reported too;
suppress the site with `#pragma warning disable MKP003`.

### MKP004

**Warning — a `DbContext` injected outside the infrastructure layer.** A constructor takes a `DbContext` in a
class whose namespace contains none of `Infrastructure`, `Persistence`, `EntityFrameworkCore`, `Repository`,
`Data`. Handlers take repositories, not the context. A heuristic: suppress it for infrastructure that lives
in another namespace.

### MKP005

**Error — a repository method returns `IQueryable<T>`.** A public or internal method of a repository returns
`IQueryable<T>` (or a task of one): the caller composes the query and the repository no longer owns what
runs. Return a materialized result, a page or a projection.

### MKP006

**Info — one query includes two or more collections without a split decision.** Two collection includes in
one query make one SQL query that repeats the rows of one collection for each row of the other (a cartesian
product). `AsSplitQuery()` loads each collection in its own query — the reads are then not one snapshot —
and `AsSingleQuery()` keeps one query on purpose. On `QueryOptions`, the options' `AsSplitQuery()` settles it.
One collection, a reference include, `Include(string)` and includes EF Core ignores (a projection, a count)
are not reported.

### MKP007

**Warning — a query runs on each iteration of a loop.** A LINQ executor over a query, an EF Core async
executor, `Find`, explicit loading, a nested `foreach` over a query or a repository read inside a `foreach`,
`await foreach` or `for` loop costs one round trip per element. Load what the loop needs in one query before
it (`Contains` over the keys, or an include). Composing a query in a loop, staging (`Add*`, `Delete*`,
`Remove*`), bulk `Execute*` writes, `ListKeysetAsync`/`ListPagedAsync` page walks, `while`/`do` loops, a `for`
without a condition and code inside a lambda or a local function are not reported.

### MKP008

**Warning — a query is materialized before it is filtered.** `ToList()`/`ToArray()` (or an awaited
`ToListAsync`/`ToArrayAsync`) on a query from a `DbSet`, directly followed by an in-memory filter
(`Where`, `First`, `Any`, `Count`, …) whose predicate would translate, loads every row and filters it in
memory. Move the filter before the materialization. It is reported only when the move is plausible: no
`Take`, `Select` or join before it, a predicate over public, settable, mapped scalar properties. Check the
move by hand — case, culture, precision and unsaved tracked values can differ between memory and SQL.
`AsEnumerable()` marks a deliberate in-memory filter.

### MKP009

**Warning — `IgnoreQueryFilters()` disables every query filter.** The parameterless overload ignores every
filter of the model, tenant filters included. Name the filters to ignore
(`IgnoreQueryFilters(["name"])`), or use `QueryOptions.WithDeleted()` for soft-deleted aggregates. It is a
warning, not an error: an anonymous filter can only be ignored by the parameterless overload.

## Public types

| Type | Package | Role |
|---|---|---|
| `IRepository<TAggregate, TId>` | Abstractions | Write side: whole-aggregate load (optionally at an expected version), add, delete, version |
| `IUnitOfWork` | Abstractions | Commit or discard the pending change set |
| `ITransactionalContext` | Abstractions | Run an operation in a transaction (retry, join) |
| `IPagedResult<T>` | Abstractions | A page of items with the total count |
| `PersistenceException` | Abstractions | Every database failure surfaced by the module |
| `IResultUnitOfWork` | Core | `TryExecuteAsync`, `TryCommitAsync`: conflicts as `Result` errors, converted after the rollback |
| `PersistenceErrors` | Core | The `PERSISTENCE.<ROOT>.<REASON>` errors, `DefaultErrorName`, `IsValidErrorName` |
| `AggregateVersionConflictException` | Core | Inner exception of a version conflict a repository detects itself |
| `IReadRepository<TAggregate>` | Core | Read side, untracked: find, list, page, keyset page — of aggregates or projections — any, count |
| `QueryOptions<TAggregate>` | Core | Specification + includes, split query, ordering, pagination or keyset, `IncludeDeleted` |
| `QueryOptionsExtensions` | Core | `WithIncludes`, `WithPagination`, `AsSplitQuery`, `WithOrdering`, `WithDeleted`, `WithKeyset` |
| `KeysetPagination<TAggregate>`, `KeysetKey` | Core | The sort keys, page size and position of a keyset page |
| `KeysetCursor` | Core | The opaque position token: `ToString`, `TryParse` |
| `KeysetPage<T>` | Core | A keyset page and the cursor of the next one |
| `PaginationOptions` | Core | A validated page request |
| `PaginationLimits` | Core | Default and maximum page sizes |
| `PagedResult<T>` | Core | `IPagedResult<T>` implementation |
| `ISpecificationEvaluator` | Core | Applies `QueryOptions` to an `IQueryable<T>` |
| `EfRepository<TAggregate, TId, TContext>` | EntityFrameworkCore | EF Core write-side base; `LoadAggregatesAsync` for typed finders |
| `EfReadRepository<TAggregate, TContext>` | EntityFrameworkCore | EF Core read-side base |
| `EfUnitOfWork<TContext>` | EntityFrameworkCore | `IUnitOfWork` + `ITransactionalContext` + `IResultUnitOfWork` over a `DbContext` |
| `ITransactionalUnitOfWork` | EntityFrameworkCore | The two contracts as one service |
| `EfSpecificationEvaluator` | EntityFrameworkCore | EF Core `ISpecificationEvaluator` |
| `EfDomainEventsProvider<TContext>` | EntityFrameworkCore | Drains the domain events of every tracked aggregate |
| `PersistenceServiceCollectionExtensions` | EntityFrameworkCore | `AddMicroKitPersistence`, `AddEntityFrameworkCore`, `AddDbContext`, `AddUnitOfWork`, `ConfigurePagination` |
| `PersistenceBuilder`, `EfCoreBuilder` | EntityFrameworkCore | Registration builders |
| `ProviderOptions`, `RetryOnFailureOptions` | EntityFrameworkCore | Opt-in retry and pooling |
| `DomainConventionExtensions` | EntityFrameworkCore | MicroKit.Domain mappings for `ConfigureConventions` |
| `MicroKitPersistenceDbContextOptionsExtensions` | EntityFrameworkCore | `UseMicroKitPersistence()`: boundary validation, the loaded-root record, the aggregate version and its `SaveChanges` step; `UseMicroKitPersistence(classifier)` adds the provider's error classifier |
| `IDatabaseFailureClassifier`, `DatabaseFailure` | EntityFrameworkCore | The seam a provider package recognises its error codes through |
| `AggregateErrorNameBuilderExtensions` | EntityFrameworkCore | `HasAggregateErrorName` on a root |
| `AggregateBoundaryNavigationBuilderExtensions` | EntityFrameworkCore | `IsOutsideAggregate()` on a navigation |
| `AggregateVersion` | EntityFrameworkCore | `PropertyName` of the version token, for projections |
| `SoftDelete`, `SoftDeleteBuilderExtensions` | EntityFrameworkCore | `HasSoftDelete()` on a root; `PropertyName` and `FilterName` of the deletion mark |
| `PostgreSqlEfCoreBuilderExtensions` | PostgreSql | `UsePostgreSQL<TContext>` |
| `PostgreSqlFailureClassifier` | PostgreSql | `23505`, `23503` |
| `SqlServerEfCoreBuilderExtensions` | SqlServer | `UseSqlServer<TContext>` |
| `SqlServerFailureClassifier` | SqlServer | `2627`, `2601`, `547` |
| `QueryOptionsOrderingExtensions` | Specifications | `WithOrderBy`, `WithOrderByDescending`, `WithThenBy`, `WithThenByDescending` |
| `QueryOptionsSpecificationExtensions` | Specifications | `WithSpec` |
| `InMemoryRepository<TAggregate, TId>` | Testing | Write and read double; versions checked at the find (`SetVersion`) |
| `InMemoryReadRepository<TAggregate>` | Testing | Read-only double |
| `InMemoryUnitOfWork` | Testing | Counts commits and discards; `IResultUnitOfWork` double |
| `DockerProbe` | Testing | `IsAvailable`: whether a Docker daemon answers, framework-neutral |
| `PostgreSqlFixture` | Testing.PostgreSql | A PostgreSQL container: `StartAsync`, `ConnectionString`, `GetConnectionString(database)`, `DisposeAsync` |
| `SqlServerFixture` | Testing.SqlServer | The same for SQL Server |
| `ReadRepositoryMutationAnalyzer`, `DirectSaveChangesAnalyzer`, `DbContextInjectionAnalyzer`, `RepositoryIQueryableLeakAnalyzer` | Analyzers | MKP001–MKP005 |
| `CollectionIncludeAnalyzer`, `QueryInLoopAnalyzer`, `MaterializeBeforeFilterAnalyzer`, `IgnoreAllQueryFiltersAnalyzer` | Analyzers | MKP006–MKP009 |

## Decisions

Architecture decisions live in
[`.claude-context/context/architectural-decisions.md`](.claude-context/context/architectural-decisions.md);
the breaking changes of each version in [`CHANGELOG.md`](CHANGELOG.md).
