using System.Linq.Expressions;

namespace MicroKit.Persistence;

/// <summary>
/// Keyset (cursor) pagination: the sort keys, the page size and the position to start after. Set it on
/// <see cref="QueryOptions{TAggregate}.Keyset"/> with <see cref="QueryOptionsExtensions.WithKeyset{TAggregate}"/>, and
/// read the page with <see cref="IReadRepository{TAggregate}.ListKeysetAsync(QueryOptions{TAggregate}, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <remarks>
/// <para>
/// Each sort key is a scalar property of the root, not nullable: a null has no place in the
/// <c>(k1 &gt; v1) OR (k1 = v1 AND k2 &gt; v2)</c> comparison that starts a page. The read repository appends the
/// root's primary key, ascending, as the last sort key, so the ordering is total and no row is skipped or
/// repeated on a tie. Paging is forward only: each page returns the cursor of the next one.
/// </para>
/// <para>
/// The builders are named after <c>Queryable</c>'s ordering operators, not with the <c>With-</c> prefix of the
/// <see cref="QueryOptions{TAggregate}"/> extensions: they build this record, they do not extend the options.
/// </para>
/// <code>
/// var first = new QueryOptions&lt;Order&gt;(new OpenOrdersSpec())
///     .WithKeyset(KeysetPagination&lt;Order&gt;.OrderByDescending(o =&gt; o.PlacedAt) with { PageSize = 20 });
/// var page = await orders.ListKeysetAsync(first, ct);
/// var next = first.WithKeyset(first.Keyset! with { After = page.Next });
/// </code>
/// </remarks>
public sealed record KeysetPagination<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private readonly int? _pageSize;

    private KeysetPagination(IReadOnlyList<KeysetKey> keys) => Keys = keys;

    /// <summary>Gets the sort keys, in order, without the primary key the read repository appends.</summary>
    public IReadOnlyList<KeysetKey> Keys { get; private init; }

    /// <summary>
    /// Gets the page size; <see langword="null"/> takes <see cref="PaginationLimits.DefaultPageSize"/>. A size
    /// above <see cref="PaginationLimits.MaxPageSize"/> is refused when the page is read.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to less than 1.</exception>
    public int? PageSize
    {
        get => _pageSize;
        init
        {
            if (value is { } size)
                ArgumentOutOfRangeException.ThrowIfLessThan(size, 1, nameof(PageSize));
            _pageSize = value;
        }
    }

    /// <summary>Gets the position to start after: the <see cref="KeysetPage{T}.Next"/> of the page before; <see langword="null"/> for the first page.</summary>
    public KeysetCursor? After { get; init; }

    /// <summary>Starts an ordering by <paramref name="key"/>, ascending.</summary>
    /// <typeparam name="TKey">The key's type; <see cref="Nullable{T}"/> is refused.</typeparam>
    /// <param name="key">A scalar property of the root.</param>
    /// <returns>The keyset pagination, first page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey"/> is nullable.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1000:Do not declare static members on generic types",
        Justification = "The ordering is started from the type, as Queryable.OrderBy starts one: the type argument is the aggregate.")]
    public static KeysetPagination<TAggregate> OrderBy<TKey>(Expression<Func<TAggregate, TKey>> key) => new([KeysetKey.Of(key, descending: false)]);

    /// <summary>Starts an ordering by <paramref name="key"/>, descending.</summary>
    /// <typeparam name="TKey">The key's type; <see cref="Nullable{T}"/> is refused.</typeparam>
    /// <param name="key">A scalar property of the root.</param>
    /// <returns>The keyset pagination, first page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey"/> is nullable.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1000:Do not declare static members on generic types",
        Justification = "The ordering is started from the type, as Queryable.OrderByDescending starts one.")]
    public static KeysetPagination<TAggregate> OrderByDescending<TKey>(Expression<Func<TAggregate, TKey>> key) => new([KeysetKey.Of(key, descending: true)]);

    /// <summary>Adds <paramref name="key"/>, ascending, after the keys already there.</summary>
    /// <typeparam name="TKey">The key's type; <see cref="Nullable{T}"/> is refused.</typeparam>
    /// <param name="key">A scalar property of the root.</param>
    /// <returns>A copy with the key added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey"/> is nullable.</exception>
    public KeysetPagination<TAggregate> ThenBy<TKey>(Expression<Func<TAggregate, TKey>> key) =>
        this with { Keys = [.. Keys, KeysetKey.Of(key, descending: false)] };

    /// <summary>Adds <paramref name="key"/>, descending, after the keys already there.</summary>
    /// <typeparam name="TKey">The key's type; <see cref="Nullable{T}"/> is refused.</typeparam>
    /// <param name="key">A scalar property of the root.</param>
    /// <returns>A copy with the key added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TKey"/> is nullable.</exception>
    public KeysetPagination<TAggregate> ThenByDescending<TKey>(Expression<Func<TAggregate, TKey>> key) =>
        this with { Keys = [.. Keys, KeysetKey.Of(key, descending: true)] };
}

/// <summary>A sort key of a <see cref="KeysetPagination{TAggregate}"/>: a property of the root and a direction.</summary>
public sealed record KeysetKey
{
    private KeysetKey(LambdaExpression selector, bool descending)
    {
        Selector = selector;
        Descending = descending;
    }

    /// <summary>Gets the key selector, a lambda over the root.</summary>
    public LambdaExpression Selector { get; }

    /// <summary>Gets a value indicating whether the key sorts descending.</summary>
    public bool Descending { get; }

    internal static KeysetKey Of<TAggregate, TKey>(Expression<Func<TAggregate, TKey>> key, bool descending)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Nullable.GetUnderlyingType(typeof(TKey)) is not null)
        {
            throw new ArgumentException(
                $"The keyset sort key '{key}' is nullable ('{typeof(TKey).Name}?'): a keyset ordering needs a value on every row. " +
                "Sort by a required property.",
                nameof(key));
        }

        return new KeysetKey(key, descending);
    }
}
