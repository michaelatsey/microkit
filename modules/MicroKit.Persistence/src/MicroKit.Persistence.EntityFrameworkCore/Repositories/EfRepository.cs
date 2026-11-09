using System.Linq.Expressions;

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
/// Extend this class for typed repositories. A typed finder loads through
/// <see cref="LoadAggregatesAsync"/>, which applies the whole-aggregate load and records the roots:
/// <code>
/// public sealed class EfOrderRepository(AppDbContext ctx)
///     : EfRepository&lt;Order, OrderId, AppDbContext&gt;(ctx), IOrderRepository
/// {
///     public async ValueTask&lt;Order?&gt; FindByNumberAsync(OrderNumber number, CancellationToken ct = default)
///     {
///         var orders = await LoadAggregatesAsync(o => o.Number == number, ct).ConfigureAwait(false);
///         return orders.SingleOrDefault();
///     }
/// }
/// </code>
/// </para>
/// <para>
/// The boundary of an aggregate is read from the model only (every navigation to an entity that
/// is not an <see cref="IAggregateRoot"/>, recursively, minus those marked
/// <c>IsOutsideAggregate()</c>); no repository can redefine it. The context must be configured with
/// <c>UseMicroKitPersistence()</c> — <c>AddDbContext</c>, <c>UsePostgreSQL</c> and
/// <c>UseSqlServer</c> do it.
/// </para>
/// <para>
/// A root the context tracks is returned only when a repository over this context loaded it whole
/// (<see cref="FindAsync(TId, CancellationToken)"/>, <see cref="LoadAggregatesAsync"/>) or staged it (<see cref="AddAsync"/>,
/// whether or not it was committed since), or when it is <c>Added</c>. A root tracked any other way — a tracking query on the <see cref="DbSet{TEntity}"/>,
/// a filtered <c>Include</c>, an explicit load, an <c>Attach</c>, a reattachment after
/// <c>ChangeTracker.Clear()</c> — may be partial, and is refused with an
/// <see cref="InvalidOperationException"/>: a partial aggregate must never reach a command.
/// </para>
/// <para>
/// The repository never commits: the command's <see cref="IUnitOfWork"/> does. There is no update
/// method: an aggregate loaded through <see cref="FindAsync(TId, CancellationToken)"/> is tracked, and its changes are
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
    /// its primary key is not a single property of type <typeparamref name="TId"/>; the context was
    /// not configured with <c>UseMicroKitPersistence()</c>; or the context already tracks this
    /// aggregate's root without a repository over this context having loaded it whole or staged it,
    /// and it is not <c>Added</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A root a repository over this context already loaded or staged (<see cref="AddAsync"/>), or
    /// any <c>Added</c> root, is returned as is — same instance, same state — without a query. Otherwise the root is loaded with every
    /// entity of its aggregate and tracked, whatever the context's default
    /// <c>QueryTrackingBehavior</c>.
    /// </para>
    /// <para>
    /// A split load issues one statement per collection. Under the default Read Committed isolation
    /// each statement sees the data committed when it starts, even inside a transaction: the root and
    /// a collection can come from two points in time. The aggregate's version
    /// (<see cref="AggregateVersion"/>) makes such a torn read fail the commit — but only when the
    /// command writes; for a read-only decision across root and children, use Snapshot or
    /// Serializable isolation.
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

        var record = KeyLookup.LoadRecordOf(Context);
        object[] keyValues = [id];

        var tracked = KeyLookup.FindTracked(Context, set, keyValues);
        if (tracked is not null)
        {
            EnsureWhole(record, tracked);
            return tracked.Entity;
        }

        var loaded = await LoadAsync(record, KeyLookup.Predicate<TAggregate>(key, keyValues), ct).ConfigureAwait(false);
        return loaded.SingleOrDefault();
    }

    /// <inheritdoc cref="IRepository{TAggregate, TId}.FindAsync(TId, long, CancellationToken)"/>
    /// <exception cref="InvalidOperationException">As for <see cref="FindAsync(TId, CancellationToken)"/>.</exception>
    /// <exception cref="PersistenceException">
    /// The aggregate is staged but not inserted yet, and <paramref name="expectedVersion"/> is not 0.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The aggregate is loaded as <see cref="FindAsync(TId, CancellationToken)"/> loads it, then
    /// <paramref name="expectedVersion"/> becomes the original value of its
    /// <see cref="AggregateVersion.PropertyName"/> token. The check runs in the <c>UPDATE</c> (or
    /// <c>DELETE</c>) of the root that the next save writes — <c>WHERE AggregateVersion = @expected</c>
    /// — never as a read-then-compare: a version that moved on fails that save with a concurrency
    /// conflict (<see cref="PersistenceException"/> wrapping <see cref="DbUpdateConcurrencyException"/>)
    /// and nothing of it is written. A stale version conflicts even when the command changes nothing.
    /// </para>
    /// <para>
    /// A root staged but not inserted yet has version 0, and an <c>INSERT</c> checks nothing: for it,
    /// the expectation is checked here, and any value other than 0 throws.
    /// </para>
    /// <para>
    /// Bulk writes (<c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, raw SQL) bypass the change tracker:
    /// they neither bump the version nor conflict with it.
    /// </para>
    /// </remarks>
    public async ValueTask<TAggregate?> FindAsync(TId id, long expectedVersion, CancellationToken ct = default)
    {
        var aggregate = await FindAsync(id, ct).ConfigureAwait(false);
        if (aggregate is null)
            return null;

        var entry = Context.Entry(aggregate);
        if (entry.State == EntityState.Added)
        {
            // Not inserted yet: its version is 0, and an INSERT checks nothing, so the check runs here.
            if (expectedVersion != 0)
            {
                throw new PersistenceException(
                    "Optimistic concurrency conflict: the expected version does not match.",
                    new InvalidOperationException(
                        $"This '{typeof(TAggregate).Name}' is not inserted yet: its version is 0, not {expectedVersion}."));
            }

            return aggregate;
        }

        entry.Property<long>(AggregateVersion.PropertyName).OriginalValue = expectedVersion;
        return aggregate;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="aggregate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context was not configured with <c>UseMicroKitPersistence()</c>, or does not track
    /// <paramref name="aggregate"/>.
    /// </exception>
    /// <remarks>
    /// The value read when the aggregate was loaded, or written by its last commit through this
    /// context; a save that fails puts the value back. An <c>Added</c> root reads 0 until its
    /// commit, which writes 1.
    /// </remarks>
    public long GetVersion(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _ = KeyLookup.LoadRecordOf(Context);
        var entry = Context.Entry(aggregate);
        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                $"This '{typeof(TAggregate).Name}' is not tracked by the context: its version is only known for an aggregate " +
                "the repository loaded or staged.");
        }

        return entry.Property<long>(AggregateVersion.PropertyName).CurrentValue;
    }

    /// <summary>
    /// Loads, tracked and whole, every aggregate whose root matches <paramref name="predicate"/>.
    /// The entry point for typed finders.
    /// </summary>
    /// <param name="predicate">The filter on the roots. It cannot shape what is loaded with them.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The matching aggregates; an empty list when none matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TAggregate"/> is not part of the context's model; the context was not
    /// configured with <c>UseMicroKitPersistence()</c>; or a matching root was already tracked
    /// without a repository over this context having loaded it whole or staged it, and is not
    /// <c>Added</c> (the query returns the tracked instance as is, so it would stay partial).
    /// </exception>
    /// <remarks>
    /// The load is that of <see cref="FindAsync(TId, CancellationToken)"/>: every entity of the aggregate, split into one
    /// statement per collection when it has two or more, with the same isolation caveat. It is not
    /// row-limited: a row limit on a split load needs an ordering, so materialise and take what
    /// you need.
    /// </remarks>
    protected async ValueTask<IReadOnlyList<TAggregate>> LoadAggregatesAsync(
        Expression<Func<TAggregate, bool>> predicate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var record = KeyLookup.LoadRecordOf(Context);
        return await LoadAsync(record, predicate, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Stages a new aggregate for insertion. Nothing is written until the unit of work commits.
    /// </summary>
    /// <remarks>
    /// The aggregate is recorded as whole: the application built it, so <see cref="FindAsync(TId, CancellationToken)"/>
    /// returns it as is, before and after the commit.
    /// </remarks>
    /// <param name="aggregate">The aggregate to insert. Must not already exist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public async ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await Context.Set<TAggregate>().AddAsync(aggregate, ct).ConfigureAwait(false);

        // Whole by construction: it stays returnable once the commit makes it Unchanged.
        KeyLookup.FindLoadRecord(Context)?.RecordAdded(aggregate);
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

    // Runs the whole-aggregate load inside a record scope and records, once the query has completed,
    // the roots it returned and tracked itself — a load that fails midway records nothing. Then checks
    // every returned root: identity resolution hands back an instance the context already tracked
    // without raising Tracked, so a partial root tracked before the load comes back unrecorded.
    private async ValueTask<List<TAggregate>> LoadAsync(
        AggregateLoadRecord record,
        Expression<Func<TAggregate, bool>> predicate,
        CancellationToken ct = default)
    {
        var set = Context.Set<TAggregate>();
        List<TAggregate> loaded;
        using (record.BeginLoad(set.EntityType))
        {
            loaded = await IncludeAggregate(set.AsTracking())
                .Where(predicate)
                .ToListAsync(ct).ConfigureAwait(false);
            record.Complete(loaded);
        }

        foreach (var aggregate in loaded)
        {
            if (!record.IsRecorded(aggregate))
                EnsureWhole(record, Context.Entry(aggregate)); // the rare path: resolve the entry only here
        }

        return loaded;
    }

    // The includes of the whole aggregate, read from the model (AggregateIncludePlan).
    private IQueryable<TAggregate> IncludeAggregate(IQueryable<TAggregate> query)
    {
        var plan = AggregateIncludePlan.For(Context.Set<TAggregate>().EntityType);

        foreach (var path in plan.Paths)
            query = query.Include(path);

        return plan.IsSplit ? query.AsSplitQuery() : query;
    }

    private static void EnsureWhole(AggregateLoadRecord record, EntityEntry<TAggregate> entry)
    {
        if (entry.State == EntityState.Added || record.IsRecorded(entry.Entity))
            return;

        throw new InvalidOperationException(
            $"The context already tracks this '{typeof(TAggregate).Name}' aggregate, but the repository did not load it " +
            "whole: it was tracked by a query outside the repository, a filtered Include, an explicit load, an Attach or " +
            "a reattachment after ChangeTracker.Clear(), and may be partial. Load aggregates through the repository " +
            "only (FindAsync, LoadAggregatesAsync), or clear the change tracker before loading.");
    }
}
