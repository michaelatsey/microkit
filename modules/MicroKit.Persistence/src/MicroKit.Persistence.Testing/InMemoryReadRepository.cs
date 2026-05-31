namespace MicroKit.Persistence.Testing;

/// <summary>
/// A read-only in-memory repository test double exposing only the query contract.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <remarks>
/// Use this double when testing query handlers that inject <see cref="IReadRepository{TAggregate}"/>
/// and you want to verify they perform no mutations. Seed with a fixed collection at construction.
/// </remarks>
public sealed class InMemoryReadRepository<TAggregate> : IReadRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private readonly InMemoryStore<TAggregate> _store;

    /// <summary>
    /// Initializes a read repository seeded with the supplied aggregates. The aggregate's
    /// identity is extracted from its conventional public <c>Id</c> property.
    /// </summary>
    /// <param name="seed">The aggregates to pre-load into the store.</param>
    public InMemoryReadRepository(IEnumerable<TAggregate> seed) =>
        _store = new InMemoryStore<TAggregate>(seed);

    /// <summary>
    /// Initializes a read repository seeded with the supplied aggregates using an explicit
    /// key selector.
    /// </summary>
    /// <param name="seed">The aggregates to pre-load into the store.</param>
    /// <param name="keySelector">
    /// Extracts the identity key. Required when the aggregate has no public <c>Id</c> property.
    /// </param>
    public InMemoryReadRepository(
        IEnumerable<TAggregate> seed,
        Func<TAggregate, object> keySelector) =>
        _store = new InMemoryStore<TAggregate>(seed, keySelector);

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
