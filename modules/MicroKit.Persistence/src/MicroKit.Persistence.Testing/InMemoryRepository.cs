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
/// A second difference: the read methods return the stored instances too, the very ones
/// <see cref="FindAsync(TId, CancellationToken)"/> hands to a command, so a pending mutation is
/// visible through them at once. <c>EfReadRepository</c> never tracks and returns new instances
/// loaded from the database: a query handler must not rely on seeing a command's uncommitted change.
/// </para>
/// <para>
/// Versions: a stored aggregate starts at version 1 and does not move on its own — the double
/// cannot see mutations. <see cref="SetVersion"/> sets another one, and
/// <see cref="FindAsync(TId, long, CancellationToken)"/> checks the expected version at the find,
/// where EF Core checks it in the <c>UPDATE</c> of the commit. A stale version throws a
/// <see cref="PersistenceException"/> whose inner <see cref="AggregateVersionConflictException"/> carries
/// the default error name of <typeparamref name="TAggregate"/> (<see cref="PersistenceErrors.DefaultErrorName"/>),
/// which <see cref="InMemoryUnitOfWork.TryExecuteAsync{TState}"/> turns into
/// <see cref="PersistenceErrors.Concurrency"/>. The double has no model, so a name the application
/// fixes with <c>HasAggregateErrorName</c> is unknown to it.
/// </para>
/// <para>
/// <see cref="QueryOptions{TAggregate}.Includes"/>,
/// <see cref="QueryOptions{TAggregate}.AsSplitQueryEnabled"/> and
/// <see cref="QueryOptions{TAggregate}.IncludeDeleted"/> are accepted but ignored — EF Core execution
/// concerns with no in-memory equivalent; the double deletes for real, so nothing is soft-deleted.
/// </para>
/// </remarks>
public sealed class InMemoryRepository<TAggregate, TId>
    : IReadRepository<TAggregate>,
      IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot
    where TId : IEntityId
{
    // The <ROOT> segment of a conflict's code; null when the CLR name cannot be one.
    private static readonly string? s_errorName =
        PersistenceErrors.DefaultErrorName(typeof(TAggregate)) is var name && PersistenceErrors.IsValidErrorName(name)
            ? name
            : null;

    private readonly InMemoryStore<TAggregate> _store;
    private readonly Dictionary<object, long> _versions = [];

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
        _versions.Remove(_store.KeyOf(aggregate));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    /// <exception cref="PersistenceException">
    /// The stored aggregate's version is not <paramref name="expectedVersion"/>; its inner exception is an
    /// <see cref="AggregateVersionConflictException"/>. The double checks at the find; EF Core checks in
    /// the <c>UPDATE</c> of the commit.
    /// </exception>
    public ValueTask<TAggregate?> FindAsync(TId id, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ct.ThrowIfCancellationRequested();
        var aggregate = _store.Find(id);
        if (aggregate is not null && VersionOf(aggregate) != expectedVersion)
        {
            throw new PersistenceException(
                "Optimistic concurrency conflict at the find (in-memory double).",
                new AggregateVersionConflictException(
                    $"'{typeof(TAggregate).Name}' {id} is at version {VersionOf(aggregate)}, not {expectedVersion}.")
                {
                    ErrorName = s_errorName,
                });
        }

        return new ValueTask<TAggregate?>(aggregate);
    }

    /// <summary>Gets the stored version of an aggregate.</summary>
    /// <param name="aggregate">A stored aggregate.</param>
    /// <returns>1 from <see cref="AddAsync"/>, or the value of the last <see cref="SetVersion"/>.</returns>
    /// <exception cref="InvalidOperationException">The aggregate is not stored.</exception>
    public long GetVersion(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return VersionOf(aggregate);
    }

    /// <summary>
    /// Sets the version of a stored aggregate, so a test can exercise a stale
    /// <see cref="FindAsync(TId, long, CancellationToken)"/>.
    /// </summary>
    /// <param name="aggregate">A stored aggregate.</param>
    /// <param name="version">Its new version.</param>
    /// <exception cref="InvalidOperationException">The aggregate is not stored.</exception>
    public void SetVersion(TAggregate aggregate, long version)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _ = VersionOf(aggregate);
        _versions[_store.KeyOf(aggregate)] = version;
    }

    private long VersionOf(TAggregate aggregate)
    {
        var key = _store.KeyOf(aggregate);
        if (!ReferenceEquals(_store.Find(key), aggregate))
            throw new InvalidOperationException($"This '{typeof(TAggregate).Name}' is not stored in the repository.");
        return _versions.TryGetValue(key, out var version) ? version : 1;
    }

    // ---- Read side (Core.IReadRepository<TAggregate>) ----

    /// <inheritdoc/>
    /// <remarks>
    /// The in-memory double supports single-element key arrays only.
    /// Composite PKs (arrays with length &gt; 1) throw <see cref="NotSupportedException"/>.
    /// Unlike <c>EfReadRepository</c>, it returns the stored instance, the one a command mutates.
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
