namespace MicroKit.Persistence.Testing;

/// <summary>
/// An in-memory repository test double that implements both the write contract
/// (<see cref="IRepository{TAggregate, TId}"/>) and the full read contract
/// (<see cref="IReadRepository{TAggregate}"/>).
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must be a reference type implementing <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identifier type.</typeparam>
/// <remarks>
/// <para>
/// Pass a single instance to both a command handler (as <see cref="IRepository{TAggregate, TId}"/>)
/// and a query handler (as <see cref="IReadRepository{TAggregate}"/>) to test them in isolation
/// without a real database — the role the shared scoped <c>DbContext</c> plays in production. It
/// therefore declares the write methods although it implements the read contract; that is the
/// one intended exception to MKP002.
/// </para>
/// <para>
/// Writes (<see cref="AddAsync"/>, <see cref="DeleteAsync"/>) are applied immediately to the
/// in-memory store, and an aggregate returned by <see cref="FindAsync(TId, CancellationToken)"/> is the
/// stored instance, so its mutations are visible at once. Use <see cref="InMemoryUnitOfWork"/>
/// to assert commit boundaries. One difference with EF Core: a deleted aggregate is gone at once,
/// whereas <c>EfRepository.FindAsync</c> still returns it, in the <c>Deleted</c> state, until the
/// unit of work commits.
/// </para>
/// <para>
/// <see cref="QueryOptions{TAggregate}.Includes"/>, <see cref="QueryOptions{TAggregate}.AsNoTrackingEnabled"/>
/// and <see cref="QueryOptions{TAggregate}.AsSplitQueryEnabled"/> are accepted but ignored — they are
/// EF Core execution concerns with no in-memory equivalent.
/// </para>
/// </remarks>
public sealed class InMemoryRepository<TAggregate, TId>
    : IReadRepository<TAggregate>,
      IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot
    where TId : IEntityId
{
    private readonly InMemoryStore<TAggregate> _store;

    /// <summary>
    /// Initializes an empty repository. The aggregate's identity is read from its conventional
    /// public <c>Id</c> property.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TAggregate"/> has no public <c>Id</c> property of type <typeparamref name="TId"/>.
    /// </exception>
    public InMemoryRepository() => _store = new InMemoryStore<TAggregate>(ConventionalKeySelector());

    /// <summary>
    /// Initializes an empty repository that enforces <paramref name="paginationLimits"/>, as the
    /// EF Core read repository does. The aggregate's identity is read from its conventional public
    /// <c>Id</c> property.
    /// </summary>
    /// <param name="paginationLimits">The page sizes to enforce.</param>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TAggregate"/> has no public <c>Id</c> property of type <typeparamref name="TId"/>.
    /// </exception>
    public InMemoryRepository(PaginationLimits paginationLimits) =>
        _store = new InMemoryStore<TAggregate>(ConventionalKeySelector(), paginationLimits
            ?? throw new ArgumentNullException(nameof(paginationLimits)));

    /// <summary>
    /// Initializes an empty repository using an explicit identity selector.
    /// </summary>
    /// <param name="idSelector">
    /// Extracts the aggregate's identifier. Required when the aggregate does not expose a public
    /// <c>Id</c> property.
    /// </param>
    public InMemoryRepository(Func<TAggregate, TId> idSelector) =>
        _store = new InMemoryStore<TAggregate>(ToKeySelector(idSelector));

    /// <summary>
    /// Initializes an empty repository using an explicit identity selector and enforcing
    /// <paramref name="paginationLimits"/>.
    /// </summary>
    /// <param name="idSelector">Extracts the aggregate's identifier.</param>
    /// <param name="paginationLimits">The page sizes to enforce.</param>
    public InMemoryRepository(Func<TAggregate, TId> idSelector, PaginationLimits paginationLimits) =>
        _store = new InMemoryStore<TAggregate>(ToKeySelector(idSelector), paginationLimits
            ?? throw new ArgumentNullException(nameof(paginationLimits)));

    /// <summary>Gets a snapshot of all aggregates currently in the store.</summary>
    public IReadOnlyCollection<TAggregate> All => _store.All;

    // ---- Write side (IRepository<TAggregate, TId>) ----

    /// <inheritdoc/>
    public ValueTask<TAggregate?> FindAsync(TId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ct.ThrowIfCancellationRequested();
        return new ValueTask<TAggregate?>(_store.Find(id));
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// An aggregate with the same identifier is already stored — EF Core rejects it too.
    /// </exception>
    public ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ct.ThrowIfCancellationRequested();
        _store.AddNew(aggregate);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ct.ThrowIfCancellationRequested();
        _store.Remove(aggregate);
        return ValueTask.CompletedTask;
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

    private static Func<TAggregate, object> ToKeySelector(Func<TAggregate, TId> idSelector)
    {
        ArgumentNullException.ThrowIfNull(idSelector);
        return aggregate => idSelector(aggregate);
    }

    private static Func<TAggregate, object> ConventionalKeySelector()
    {
        var property = typeof(TAggregate).GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property is null || property.PropertyType != typeof(TId))
        {
            throw new InvalidOperationException(
                $"'{typeof(TAggregate).Name}' has no public 'Id' property of type '{typeof(TId).Name}'. " +
                "Pass an idSelector to the repository constructor.");
        }

        return aggregate => property.GetValue(aggregate)
            ?? throw new InvalidOperationException($"The 'Id' property of '{typeof(TAggregate).Name}' returned null.");
    }
}
