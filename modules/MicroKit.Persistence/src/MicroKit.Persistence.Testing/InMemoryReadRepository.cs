using System.Linq.Expressions;

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
/// <see cref="QueryOptions{TAggregate}.IncludeDeleted"/> is accepted and ignored: the doubles hold no
/// soft-deleted aggregate.
/// </remarks>
public sealed class InMemoryReadRepository<TAggregate> : IReadRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private readonly InMemoryStore<TAggregate> _store;

    /// <summary>Gets how many keyset cursors the store holds (tests: the table stays bounded).</summary>
    internal int IssuedCursorCount => _store.IssuedCursorCount;

    /// <summary>
    /// Initializes a read repository seeded with the supplied aggregates. The aggregate's
    /// identity is extracted from its conventional public <c>Id</c> property.
    /// </summary>
    /// <param name="seed">The aggregates to pre-load into the store.</param>
    public InMemoryReadRepository(IEnumerable<TAggregate> seed) =>
        _store = new InMemoryStore<TAggregate>(seed);

    /// <summary>
    /// Initializes a read repository seeded with the supplied aggregates that enforces
    /// <paramref name="paginationLimits"/>, as the EF Core read repository does.
    /// </summary>
    /// <param name="seed">The aggregates to pre-load into the store.</param>
    /// <param name="paginationLimits">The page sizes to enforce.</param>
    public InMemoryReadRepository(IEnumerable<TAggregate> seed, PaginationLimits paginationLimits) =>
        _store = new InMemoryStore<TAggregate>(seed, paginationLimits: paginationLimits
            ?? throw new ArgumentNullException(nameof(paginationLimits)));

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

    /// <summary>
    /// Initializes a read repository seeded with the supplied aggregates using an explicit key
    /// selector and enforcing <paramref name="paginationLimits"/>.
    /// </summary>
    /// <param name="seed">The aggregates to pre-load into the store.</param>
    /// <param name="keySelector">Extracts the identity key.</param>
    /// <param name="paginationLimits">The page sizes to enforce.</param>
    public InMemoryReadRepository(
        IEnumerable<TAggregate> seed,
        Func<TAggregate, object> keySelector,
        PaginationLimits paginationLimits) =>
        _store = new InMemoryStore<TAggregate>(seed, keySelector, paginationLimits
            ?? throw new ArgumentNullException(nameof(paginationLimits)));

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
    /// <remarks>The selector is compiled and run over the in-memory set.</remarks>
    public ValueTask<IReadOnlyList<TResult>> ListAsync<TResult>(
        QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<TResult>>(_store.ExecuteList(opts, selector));
    }

    /// <inheritdoc/>
    /// <remarks>The selector is compiled and run over the in-memory page.</remarks>
    public ValueTask<IPagedResult<TResult>> ListPagedAsync<TResult>(
        QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<IPagedResult<TResult>>(_store.ExecuteListPaged(opts, selector));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The double orders in memory — strings ordinally, a key that is not <see cref="IComparable"/> (a record-struct
    /// id) by its string form — and appends its key selector as the last sort key. It has no value converters, so its
    /// cursor holds a handle into the double: a cursor is valid only on the double that issued it. A nullable sort key
    /// is refused through the property's nullability annotations.
    /// </remarks>
    public ValueTask<KeysetPage<TAggregate>> ListKeysetAsync(QueryOptions<TAggregate> opts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(opts);
        return new ValueTask<KeysetPage<TAggregate>>(_store.ExecuteKeyset(opts, static item => item));
    }

    /// <inheritdoc/>
    /// <remarks>As <see cref="ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>, with the selector compiled.</remarks>
    public ValueTask<KeysetPage<TResult>> ListKeysetAsync<TResult>(
        QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(selector);
        return new ValueTask<KeysetPage<TResult>>(_store.ExecuteKeyset(opts, selector.Compile()));
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
