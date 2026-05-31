using Abs = MicroKit.Persistence.Abstractions;

namespace MicroKit.Persistence.Testing;

/// <summary>
/// An in-memory repository test double that implements both the write contract
/// (<see cref="Abs.IRepository{TAggregate}"/>) and the full read contract
/// (<see cref="IReadRepository{TAggregate}"/>).
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Pass a single instance to both a command handler (as <see cref="Abs.IRepository{TAggregate}"/>)
/// and a query handler (as <see cref="IReadRepository{TAggregate}"/>) to test them in isolation
/// without a real database.
/// </para>
/// <para>
/// Writes (<see cref="AddAsync"/>, <see cref="UpdateAsync"/>, <see cref="DeleteAsync"/>) are
/// applied immediately to the in-memory store. <see cref="CommitAsync"/> on this type honours
/// cancellation only — use <see cref="InMemoryUnitOfWork"/> to track commit boundaries.
/// </para>
/// <para>
/// <see cref="QueryOptions{TAggregate}.Includes"/>, <see cref="QueryOptions{TAggregate}.AsNoTrackingEnabled"/>,
/// <see cref="QueryOptions{TAggregate}.AsSplitQueryEnabled"/>, and
/// <see cref="QueryOptions{TAggregate}.IncludeDeleted"/> are accepted but ignored — they are
/// EF Core execution concerns with no in-memory equivalent.
/// </para>
/// </remarks>
public sealed class InMemoryRepository<TAggregate>
    : IReadRepository<TAggregate>,
      Abs.IRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private readonly InMemoryStore<TAggregate> _store;

    /// <summary>
    /// Initializes an empty repository. The aggregate's identity is extracted from its
    /// conventional public <c>Id</c> property.
    /// </summary>
    public InMemoryRepository() => _store = new InMemoryStore<TAggregate>();

    /// <summary>
    /// Initializes an empty repository using an explicit key selector.
    /// </summary>
    /// <param name="keySelector">
    /// Extracts the identity key used for storage and lookup. Required when the aggregate
    /// does not expose a public <c>Id</c> property.
    /// </param>
    public InMemoryRepository(Func<TAggregate, object> keySelector) =>
        _store = new InMemoryStore<TAggregate>(keySelector);

    /// <summary>Gets a snapshot of all aggregates currently in the store.</summary>
    public IReadOnlyCollection<TAggregate> All => _store.All;

    // ---- Write side (Abs.IRepository<TAggregate>) ----

    /// <inheritdoc/>
    public ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.Add(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask UpdateAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.Update(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.Remove(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// No-op commit boundary. Honours cancellation; otherwise returns immediately.
    /// Use <see cref="InMemoryUnitOfWork"/> to track commit boundaries in tests.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    // ---- Convenience finder (not on any interface — avoids overload ambiguity with object[]) ----

    /// <summary>
    /// Finds an aggregate by a single identity key.
    /// </summary>
    /// <param name="id">The identity key. Must not be an <c>object[]</c> — use
    /// <see cref="FindAsync(object[], CancellationToken)"/> for composite PKs.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The matching aggregate, or <see langword="null"/> if not found.</returns>
    public ValueTask<TAggregate?> FindById(object id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<TAggregate?>(_store.Find(id));
    }

    // ---- Read side (Core.IReadRepository<TAggregate>) ----

    /// <inheritdoc/>
    /// <remarks>
    /// The in-memory double supports single-element key arrays only.
    /// Composite PKs (arrays with length &gt; 1) throw <see cref="NotSupportedException"/>.
    /// </remarks>
    public ValueTask<TAggregate?> FindAsync(object[] keyValues, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<TAggregate?>(_store.FindByKeys(keyValues));
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<TAggregate>> ListAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<TAggregate>>(_store.ExecuteList(opts));
    }

    /// <inheritdoc/>
    public ValueTask<IPagedResult<TAggregate>> ListPagedAsync(
        QueryOptions<TAggregate> opts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<IPagedResult<TAggregate>>(_store.ExecuteListPaged(opts));
    }

    /// <inheritdoc/>
    public ValueTask<bool> AnyAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<bool>(_store.ExecuteAny(opts));
    }

    /// <inheritdoc/>
    public ValueTask<int> CountAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<int>(_store.ExecuteCount(opts));
    }
}
