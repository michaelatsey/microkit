namespace MicroKit.Persistence.Specifications;

/// <summary>
/// Extension method for replacing the <see cref="Specification{T}"/> on an existing
/// <see cref="QueryOptions{TAggregate}"/> instance.
/// </summary>
public static class QueryOptionsSpecificationExtensions
{
    /// <summary>
    /// Returns a new <see cref="QueryOptions{TAggregate}"/> with the specification replaced
    /// by <paramref name="specification"/>. All other options (includes, pagination, ordering,
    /// tracking, split-query, soft-delete) are preserved unchanged.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <param name="opts">The source options to derive from.</param>
    /// <param name="specification">
    ///   The replacement specification, or <see langword="null"/> to clear the filter and
    ///   return all aggregates (subject to soft-delete filtering).
    /// </param>
    /// <returns>
    ///   A new options instance with <see cref="QueryOptions{TAggregate}.Specification"/>
    ///   set to <paramref name="specification"/>.
    /// </returns>
    public static QueryOptions<TAggregate> WithSpec<TAggregate>(
        this QueryOptions<TAggregate> opts,
        Specification<TAggregate>? specification)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(opts);
        return opts with { Specification = specification };
    }
}
