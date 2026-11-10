using System.Linq.Expressions;
using System.Reflection;

namespace MicroKit.Persistence.Testing;

/// <summary>
/// Internal backing store shared by <see cref="InMemoryRepository{TAggregate, TId}"/> and
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
    private readonly PaginationLimits _paginationLimits;

    // The cursors this store issued. The double has no value converters to turn a key into a provider value, so
    // its cursor carries a handle into this table: a cursor is valid only on the double that issued it. A row's
    // handle is reused (keyed by its sort-key values), so the table is bounded by the distinct rows paged.
    private readonly Dictionary<long, object?[]> _cursorKeys = [];
    private readonly Dictionary<object?[], long> _cursorHandles = new(RowKeysComparer.Instance);
    private long _lastCursor;

    /// <summary>Gets how many cursors the store holds.</summary>
    internal int IssuedCursorCount => _cursorKeys.Count;

    internal InMemoryStore(Func<TAggregate, object>? keySelector = null, PaginationLimits? paginationLimits = null)
    {
        _paginationLimits = paginationLimits ?? PaginationLimits.Default;
        _keySelector = keySelector
            ?? s_conventionalKey
            ?? throw new InvalidOperationException(
                $"'{typeof(TAggregate).Name}' has no public 'Id' property. " +
                "Provide an explicit keySelector to the repository constructor.");
    }

    internal InMemoryStore(
        IEnumerable<TAggregate> seed,
        Func<TAggregate, object>? keySelector = null,
        PaginationLimits? paginationLimits = null)
        : this(keySelector, paginationLimits)
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

        // A null key value matches nothing, as EfReadRepository.FindAsync and DbSet.Find answer.
        return keys[0] is { } key ? Find(key) : null;
    }

    internal void AddNew(TAggregate item)
    {
        if (!_data.TryAdd(_keySelector(item), item))
        {
            throw new InvalidOperationException(
                $"A '{typeof(TAggregate).Name}' with the identifier '{_keySelector(item)}' is already stored.");
        }
    }

    internal void Remove(TAggregate item) => _data.Remove(_keySelector(item));

    internal object KeyOf(TAggregate item) => _keySelector(item);

    // ---- In-memory query pipeline ----

    // Mirrors EfReadRepository: same PaginationLimits rules, so a handler tested against the
    // double sees the bounded default page and the maximum-size rejection it meets in production.
    internal IReadOnlyList<TAggregate> ExecuteList(QueryOptions<TAggregate> opts)
    {
        KeysetRules.EnsureNone(opts, "ListAsync");
        _paginationLimits.EnsureWithinMaximum(opts.Pagination);
        var items = ApplyFilter(opts);
        items = ApplyOrder(items, opts);
        if (opts.Pagination is { } p)
            items = items.Skip(p.Skip).Take(p.PageSize);
        return items.ToList().AsReadOnly();
    }

    internal IPagedResult<TAggregate> ExecuteListPaged(QueryOptions<TAggregate> opts)
    {
        KeysetRules.EnsureNone(opts, "ListPagedAsync");
        var pagination = _paginationLimits.ForPagedQuery(opts.Pagination);
        var all = ApplyOrder(ApplyFilter(opts), opts).ToList();

        var page = all.Skip(pagination.Skip).Take(pagination.PageSize).ToList().AsReadOnly();
        return new PagedResult<TAggregate>(page, all.Count, pagination.Page, pagination.PageSize);
    }

    // A projection is the selector compiled over the same pipeline: the includes it would drop mean nothing here.
    internal IReadOnlyList<TResult> ExecuteList<TResult>(QueryOptions<TAggregate> opts, Expression<Func<TAggregate, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var project = selector.Compile();
        return ExecuteList(opts).Select(project).ToList().AsReadOnly();
    }

    internal IPagedResult<TResult> ExecuteListPaged<TResult>(QueryOptions<TAggregate> opts, Expression<Func<TAggregate, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var project = selector.Compile();
        var page = ExecuteListPaged(opts);
        return new PagedResult<TResult>(page.Items.Select(project).ToList().AsReadOnly(), page.TotalCount, page.Page, page.PageSize);
    }

    // Mirrors EfReadRepository.ListKeysetAsync: the same rules, ordering by the sort keys then the store's key,
    // starting strictly after the cursor's row, pageSize + 1 rows read to know whether a page follows.
    internal KeysetPage<TResult> ExecuteKeyset<TResult>(QueryOptions<TAggregate> opts, Func<TAggregate, TResult> project)
    {
        var keyset = KeysetRules.Require(opts);
        var pageSize = _paginationLimits.ForKeysetQuery(keyset.PageSize);
        var keys = keyset.Keys.Select(k => KeyOf(k, nameof(opts))).Append(new SortKey(StoreKeyName, false, _keySelector)).ToList();
        var ordering = keys.Select(k => (k.Name, k.Descending)).ToList();
        var root = typeof(TAggregate).FullName ?? typeof(TAggregate).Name;

        var rows = ApplyFilter(opts).Select(item => (Item: item, Keys: keys.Select(k => k.Value(item)).ToArray())).ToList();
        rows.Sort((x, y) => CompareRows(keys, x.Keys, y.Keys));
        IEnumerable<(TAggregate Item, object?[] Keys)> window = rows;
        if (keyset.After is { } after)
        {
            if (!after.IsFor(root, ordering))
            {
                throw new ArgumentException(
                    $"The cursor was issued for another aggregate type or another ordering than this '{typeof(TAggregate).Name}' keyset.",
                    nameof(opts));
            }

            if (after.Values is not [long handle] || !_cursorKeys.TryGetValue(handle, out var last))
                throw new ArgumentException("The cursor was not issued by this in-memory repository.", nameof(opts));
            window = rows.Where(row => CompareRows(keys, row.Keys, last) > 0);
        }

        var read = window.Take(pageSize + 1).ToList();
        var page = read.Take(pageSize).ToList();
        KeysetCursor? next = null;
        if (read.Count > pageSize)
        {
            var last = page[^1].Keys;
            if (!_cursorHandles.TryGetValue(last, out var handle))
            {
                handle = ++_lastCursor;
                _cursorHandles[last] = handle;
                _cursorKeys[handle] = last;
            }

            next = KeysetCursor.Create(root, ordering, [handle]);
        }

        return new KeysetPage<TResult>(page.Select(row => project(row.Item)).ToList().AsReadOnly(), pageSize, next);
    }

    internal bool ExecuteAny(QueryOptions<TAggregate> opts)
    {
        KeysetRules.EnsureNone(opts, "AnyAsync");
        return ApplyFilter(opts).Any();
    }

    internal int ExecuteCount(QueryOptions<TAggregate> opts)
    {
        KeysetRules.EnsureNone(opts, "CountAsync");
        return ApplyFilter(opts).Count();
    }

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

    private const string StoreKeyName = "<store key>";

    /// <summary>Compares a row's sort-key values element by element (arrays structurally).</summary>
    private sealed class RowKeysComparer : IEqualityComparer<object?[]>
    {
        public static readonly RowKeysComparer Instance = new();

        public bool Equals(object?[]? x, object?[]? y) =>
            System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

        public int GetHashCode(object?[] obj) =>
            System.Collections.StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
    }

    private sealed record SortKey(string Name, bool Descending, Func<TAggregate, object?> Value);

    private static readonly NullabilityInfoContext s_nullability = new();

    // A sort key is a property or field of the root, not nullable — the rule EfReadRepository applies through the
    // model; here, through the member's nullability annotations.
    private static SortKey KeyOf(KeysetKey key, string optionsName)
    {
        // Unwrapped: a member reached through an interface reads ((IFace)x).Member.
        if (key.Selector.Body is not MemberExpression { Member: var member, Expression: { } target } || Unwrap(target) != key.Selector.Parameters[0])
        {
            throw new ArgumentException(
                $"The keyset sort key '{key.Selector}' is not a property of '{typeof(TAggregate).Name}'.", optionsName);
        }

        var nullability = member switch
        {
            PropertyInfo property => s_nullability.Create(property).ReadState,
            FieldInfo field => s_nullability.Create(field).ReadState,
            _ => NullabilityState.Unknown,
        };
        if (nullability == NullabilityState.Nullable)
        {
            throw new ArgumentException(
                $"The keyset sort key '{typeof(TAggregate).Name}.{member.Name}' is nullable: a keyset ordering needs a value on every row.",
                optionsName);
        }

        var parameter = key.Selector.Parameters[0];
        var value = Expression.Lambda<Func<TAggregate, object?>>(Expression.Convert(key.Selector.Body, typeof(object)), parameter).Compile();
        return new SortKey(member.Name, key.Descending, value);
    }

    private static Expression Unwrap(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs, Operand: var operand } ? Unwrap(operand) : expression;

    private static int CompareRows(IReadOnlyList<SortKey> keys, object?[] x, object?[] y)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            var order = CompareValues(x[i], y[i]);
            if (order != 0)
                return keys[i].Descending ? -order : order;
        }

        return 0;
    }

    // Strings compare ordinally; a value that is not IComparable (a record-struct id) by its string form, which is
    // deterministic for records. The database orders by its own collation and provider values.
    private static int CompareValues(object? x, object? y) => (x, y) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string a, string b) => string.CompareOrdinal(a, b),
        (IComparable a, _) when a.GetType() == y.GetType() => a.CompareTo(y),
        _ => string.CompareOrdinal(x.ToString(), y.ToString()),
    };

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
