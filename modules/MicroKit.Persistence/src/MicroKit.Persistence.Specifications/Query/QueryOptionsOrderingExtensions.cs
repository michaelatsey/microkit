namespace MicroKit.Persistence.Specifications;

/// <summary>
/// Property-selector ordering extensions for <see cref="QueryOptions{TAggregate}"/>.
/// Complement the delegate-based <see cref="QueryOptionsExtensions.OrderBy{TAggregate}"/>
/// in <c>MicroKit.Persistence</c> with concise key-selector overloads using the
/// established <c>With-</c> prefix convention.
/// Each method derives a new record instance — the original is not modified.
/// </summary>
/// <remarks>
/// Methods are prefixed with <c>With</c> (e.g., <see cref="WithOrderBy{TAggregate, TKey}"/>)
/// rather than named <c>OrderBy</c> directly, because <see cref="QueryOptions{TAggregate}"/>
/// already exposes a delegate property named <c>OrderBy</c>. Sharing the name would cause
/// C# to resolve the call as a delegate invocation on the property rather than as an
/// extension method, producing a compile-time error.
/// </remarks>
public static class QueryOptionsOrderingExtensions
{
    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> ordered ascending by the
    /// selected property key, replacing any previously set ordering.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <typeparam name="TKey">The type of the property used for ordering.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="keySelector">
    ///   An expression that selects the sort property.
    ///   Example: <c>u =&gt; u.CreatedAt</c>.
    /// </param>
    /// <returns>
    ///   A new options instance with <see cref="QueryOptions{TAggregate}.OrderBy"/> set
    ///   to ascending order on <paramref name="keySelector"/>.
    /// </returns>
    public static QueryOptions<TAggregate> WithOrderBy<TAggregate, TKey>(
        this QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TKey>> keySelector)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(keySelector);
        return opts with { OrderBy = q => q.OrderBy(keySelector) };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> ordered descending by the
    /// selected property key, replacing any previously set ordering.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <typeparam name="TKey">The type of the property used for ordering.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="keySelector">
    ///   An expression that selects the sort property.
    ///   Example: <c>u =&gt; u.Email</c>.
    /// </param>
    /// <returns>
    ///   A new options instance with <see cref="QueryOptions{TAggregate}.OrderBy"/> set
    ///   to descending order on <paramref name="keySelector"/>.
    /// </returns>
    public static QueryOptions<TAggregate> WithOrderByDescending<TAggregate, TKey>(
        this QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TKey>> keySelector)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(keySelector);
        return opts with { OrderBy = q => q.OrderByDescending(keySelector) };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with a secondary ascending sort
    /// key appended to the existing ordering.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <typeparam name="TKey">The type of the property used for the secondary sort.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="keySelector">
    ///   An expression that selects the secondary sort property.
    ///   Example: <c>u =&gt; u.LastName</c>.
    /// </param>
    /// <returns>
    ///   A new options instance with <see cref="QueryOptions{TAggregate}.OrderBy"/> updated
    ///   to include a secondary ascending sort. Multiple <c>WithThenBy</c> calls may be chained.
    /// </returns>
    /// <remarks>
    ///   If no primary ordering has been set (<see cref="QueryOptions{TAggregate}.OrderBy"/>
    ///   is <see langword="null"/>), this method falls back to ascending <c>OrderBy</c> on
    ///   <paramref name="keySelector"/> rather than throwing. This prevents a deferred
    ///   translation error when an EF Core provider receives an unordered <c>IQueryable</c>
    ///   with a <c>ThenBy</c> applied.
    /// </remarks>
    public static QueryOptions<TAggregate> WithThenBy<TAggregate, TKey>(
        this QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TKey>> keySelector)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(keySelector);
        var existing = opts.OrderBy;
        return existing is null
            ? opts with { OrderBy = q => q.OrderBy(keySelector) }
            : opts with { OrderBy = q => existing(q).ThenBy(keySelector) };
    }

    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with a secondary descending sort
    /// key appended to the existing ordering.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <typeparam name="TKey">The type of the property used for the secondary sort.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="keySelector">
    ///   An expression that selects the secondary sort property.
    ///   Example: <c>u =&gt; u.CreatedAt</c>.
    /// </param>
    /// <returns>
    ///   A new options instance with <see cref="QueryOptions{TAggregate}.OrderBy"/> updated
    ///   to include a secondary descending sort.
    /// </returns>
    /// <remarks>
    ///   If no primary ordering has been set, falls back to <c>OrderByDescending</c> on
    ///   <paramref name="keySelector"/>. See <see cref="WithThenBy{TAggregate, TKey}"/> remarks.
    /// </remarks>
    public static QueryOptions<TAggregate> WithThenByDescending<TAggregate, TKey>(
        this QueryOptions<TAggregate> opts,
        Expression<Func<TAggregate, TKey>> keySelector)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(keySelector);
        var existing = opts.OrderBy;
        return existing is null
            ? opts with { OrderBy = q => q.OrderByDescending(keySelector) }
            : opts with { OrderBy = q => existing(q).ThenByDescending(keySelector) };
    }
}
