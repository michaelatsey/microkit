namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Generic EF Core write-side repository base implementing
/// <see cref="IRepository{TAggregate, TId}"/>.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>;
/// it is identified through its EF Core model's single-property primary key, whose CLR type must be
/// <typeparamref name="TId"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identifier type.</typeparam>
/// <typeparam name="TContext">The application <see cref="DbContext"/> type.</typeparam>
/// <param name="context">The scoped application <see cref="DbContext"/>.</param>
/// <remarks>
/// <para>
/// Extend this class for typed repositories:
/// <code>
/// public sealed class EfOrderRepository(AppDbContext ctx)
///     : EfRepository&lt;Order, OrderId, AppDbContext&gt;(ctx), IOrderRepository
/// {
///     public async ValueTask&lt;Order?&gt; FindByNumberAsync(OrderNumber number, CancellationToken ct = default)
///     {
///         // Tracked, whole, and unlimited: a row limit on a split load needs an ordering.
///         var orders = await IncludeAggregate(Context.Orders.AsTracking())
///             .Where(o => o.Number == number)
///             .ToListAsync(ct)
///             .ConfigureAwait(false);
///         return orders.SingleOrDefault();
///     }
/// }
/// </code>
/// </para>
/// <para>
/// The repository never commits: the command's <see cref="IUnitOfWork"/> does. There is no update
/// method: an aggregate loaded through <see cref="FindAsync"/> is tracked, and its changes are
/// written by the next commit.
/// </para>
/// </remarks>
public class EfRepository<TAggregate, TId, TContext>(TContext context)
    : IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot
    where TId : IEntityId
    where TContext : DbContext
{
    /// <summary>
    /// The underlying <typeparamref name="TContext"/> DbContext.
    /// Available to typed subclasses for aggregate-specific query methods.
    /// </summary>
    protected TContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Loads the whole aggregate identified by <paramref name="id"/>, tracked.
    /// </summary>
    /// <param name="id">The aggregate's identifier.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The aggregate, or <see langword="null"/> if none has this identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TAggregate"/> is not part of the context's model, has no primary key, or
    /// its primary key is not a single property of type <typeparamref name="TId"/>; or the aggregate
    /// boundary cannot be expressed as includes (two derived types declare a navigation of the same
    /// name to different types).
    /// </exception>
    /// <remarks>
    /// <para>
    /// An aggregate the unit of work already tracks is returned as is — same instance, same state —
    /// without a query. Otherwise the root is loaded with every entity of the aggregate, as
    /// <see cref="IncludeAggregate"/> describes, and tracked.
    /// </para>
    /// <para>
    /// An aggregate first tracked through a tracking read that did not load all of it is returned
    /// as tracked; it is not completed. The load is tracking whatever the context's default
    /// <c>QueryTrackingBehavior</c>.
    /// </para>
    /// </remarks>
    public async ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var set = Context.Set<TAggregate>();
        var key = KeyLookup.PrimaryKeyOf(set.EntityType);
        if (key.Count != 1 || key[0].ClrType != typeof(TId))
        {
            throw new InvalidOperationException(
                $"'{typeof(TAggregate).Name}' cannot be loaded by '{typeof(TId).Name}': its primary key is " +
                $"({string.Join(", ", key.Select(p => $"{p.ClrType.Name} {p.Name}"))}), not a single '{typeof(TId).Name}' property.");
        }

        object[] keyValues = [id];

        var tracked = KeyLookup.FindTracked(Context, set, keyValues);
        if (tracked is not null)
            return tracked.Entity;

        // Tracking is re-applied after IncludeAggregate: an override that rebuilds the query must not
        // turn the write-side load into an untracked one. The key is unique: materialise and take the
        // single row, rather than a row-limited query, which EF Core warns about on an unordered split.
        var loaded = await IncludeAggregate(set.AsTracking())
            .AsTracking()
            .Where(KeyLookup.Predicate<TAggregate>(key, keyValues))
            .ToListAsync(ct).ConfigureAwait(false);
        return loaded.SingleOrDefault();
    }

    /// <summary>
    /// Stages a new aggregate for insertion. Nothing is written until the unit of work commits.
    /// </summary>
    /// <param name="aggregate">The aggregate to insert. Must not already exist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await Context.Set<TAggregate>().AddAsync(aggregate, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stages an aggregate for deletion. Nothing is written until the unit of work commits.
    /// </summary>
    /// <param name="aggregate">The aggregate to delete.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        Context.Set<TAggregate>().Remove(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Applies the includes that load a whole <typeparamref name="TAggregate"/> to
    /// <paramref name="query"/>. <see cref="FindAsync"/> calls it; typed finders should too.
    /// </summary>
    /// <param name="query">A query over the aggregate roots.</param>
    /// <returns><paramref name="query"/> with the aggregate's includes applied.</returns>
    /// <remarks>
    /// <para>
    /// The default reads the aggregate boundary from the EF Core model, once per entity type: every
    /// navigation whose target is not an <see cref="IAggregateRoot"/> is followed, recursively —
    /// navigations of derived types and skip navigations included, a cycle detected against the
    /// ancestors of the current path only. Owned entities are loaded by EF Core itself. With two or
    /// more collection navigations (owned collections mapped to a table included) the load is split
    /// (<c>AsSplitQuery</c>); otherwise the query is left to the context's
    /// <c>UseQuerySplittingBehavior</c>.
    /// </para>
    /// <para>
    /// A split load issues one statement per collection. Under the default Read Committed isolation
    /// each statement sees the data committed when it starts, even inside a transaction: the root and
    /// a collection can come from two points in time. Use Snapshot or Serializable isolation, or a
    /// concurrency token on the root bumped by every change to the aggregate, when an invariant
    /// spans the root and its children.
    /// </para>
    /// <para>
    /// An override returns a query over the same rows, tracked, with no filter and no row limit:
    /// <see cref="FindAsync"/> adds the key filter, and the typed finders their own.
    /// </para>
    /// </remarks>
    protected virtual IQueryable<TAggregate> IncludeAggregate(IQueryable<TAggregate> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = AggregateIncludePlan.For(Context.Set<TAggregate>().EntityType);

        foreach (var path in plan.Paths)
            query = query.Include(path);

        return plan.IsSplit ? query.AsSplitQuery() : query;
    }
}
