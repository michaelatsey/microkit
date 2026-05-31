using System.Reflection;

namespace MicroKit.Persistence.Testing;

/// <summary>
/// Internal backing store shared by <see cref="InMemoryRepository{TAggregate}"/> and
/// <see cref="InMemoryReadRepository{TAggregate}"/>. Encapsulates the dictionary, key
/// extraction, and the in-memory query pipeline.
/// </summary>
internal sealed class InMemoryStore<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    // Cached once per closed generic type — avoids per-call reflection on the conventional Id property.
    private static readonly Func<TAggregate, object>? s_conventionalKey = BuildConventionalKey();

    private readonly Dictionary<object, TAggregate> _data = [];
    private readonly Func<TAggregate, object> _keySelector;

    internal InMemoryStore(Func<TAggregate, object>? keySelector = null)
    {
        _keySelector = keySelector
            ?? s_conventionalKey
            ?? throw new InvalidOperationException(
                $"'{typeof(TAggregate).Name}' has no public 'Id' property. " +
                "Provide an explicit keySelector to the repository constructor.");
    }

    internal InMemoryStore(IEnumerable<TAggregate> seed, Func<TAggregate, object>? keySelector = null)
        : this(keySelector)
    {
        foreach (var item in seed)
            _data[_keySelector(item)] = item;
    }

    internal IReadOnlyCollection<TAggregate> All => _data.Values.ToList().AsReadOnly();

    internal TAggregate? Find(object key) =>
        _data.TryGetValue(key, out var item) ? item : null;

    // Composite PKs (arrays with length > 1) are not supported in the in-memory double.
    // Throw NotSupportedException rather than returning null to give test authors early,
    // actionable feedback instead of a silent miss.
    internal TAggregate? FindByKeys(object[] keys)
    {
        if (keys.Length != 1)
            throw new NotSupportedException(
                $"InMemoryRepository does not support composite primary keys. " +
                $"Received {keys.Length} key values for '{typeof(TAggregate).Name}'.");

        return Find(keys[0]);
    }

    internal void Add(TAggregate item) => _data[_keySelector(item)] = item;

    internal void Update(TAggregate item) => _data[_keySelector(item)] = item;

    internal void Remove(TAggregate item) => _data.Remove(_keySelector(item));

    internal object KeyOf(TAggregate item) => _keySelector(item);

    // ---- In-memory query pipeline ----

    internal IReadOnlyList<TAggregate> ExecuteList(QueryOptions<TAggregate> opts)
    {
        var items = ApplyFilter(opts);
        items = ApplyOrder(items, opts);
        if (opts.Pagination is { } p)
            items = items.Skip(p.Skip).Take(p.PageSize);
        return items.ToList().AsReadOnly();
    }

    internal IPagedResult<TAggregate> ExecuteListPaged(QueryOptions<TAggregate> opts)
    {
        var filtered = ApplyFilter(opts);
        filtered = ApplyOrder(filtered, opts);
        var all = filtered.ToList();
        var total = all.Count;

        if (total == 0)
        {
            var emptyPage = opts.Pagination?.Page ?? 1;
            var emptySize = opts.Pagination?.PageSize ?? 0;
            return PagedResult<TAggregate>.Empty(emptyPage, emptySize);
        }

        if (opts.Pagination is { } p)
        {
            var page = all.Skip(p.Skip).Take(p.PageSize).ToList().AsReadOnly();
            return new PagedResult<TAggregate>(page, total, p.Page, p.PageSize);
        }

        return new PagedResult<TAggregate>(all.AsReadOnly(), total, 1, total);
    }

    internal bool ExecuteAny(QueryOptions<TAggregate> opts) =>
        ApplyFilter(opts).Any();

    internal int ExecuteCount(QueryOptions<TAggregate> opts) =>
        ApplyFilter(opts).Count();

    // Applies the specification filter; null spec = match all.
    private IEnumerable<TAggregate> ApplyFilter(QueryOptions<TAggregate> opts)
    {
        IEnumerable<TAggregate> items = _data.Values;
        if (opts.Specification is { } spec)
            items = items.Where(spec.IsSatisfiedBy);
        return items;
    }

    // Applies the OrderBy delegate via AsQueryable (LINQ to Objects — works for simple property ordering in tests).
    private static IEnumerable<TAggregate> ApplyOrder(
        IEnumerable<TAggregate> items,
        QueryOptions<TAggregate> opts)
    {
        if (opts.OrderBy is { } order)
            return order(items.AsQueryable());
        return items;
    }

    private static Func<TAggregate, object>? BuildConventionalKey()
    {
        var prop = typeof(TAggregate).GetProperty(
            "Id", BindingFlags.Public | BindingFlags.Instance);

        if (prop is null) return null;

        return agg => prop.GetValue(agg)
            ?? throw new InvalidOperationException(
                $"The 'Id' property of '{typeof(TAggregate).Name}' returned null.");
    }
}
