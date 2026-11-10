namespace MicroKit.Persistence;

/// <summary>
/// The rules the module's <see cref="IReadRepository{TAggregate}"/> implementations apply to keyset options: a keyset
/// owns the ordering and the page, and only <c>ListKeysetAsync</c> reads it.
/// </summary>
internal static class KeysetRules
{
    /// <summary>Returns the keyset of <paramref name="opts"/>, alone.</summary>
    /// <exception cref="ArgumentException">No keyset is set, or an offset pagination or an ordering is set too.</exception>
    internal static KeysetPagination<TAggregate> Require<TAggregate>(QueryOptions<TAggregate> opts)
        where TAggregate : class, IAggregateRoot
    {
        if (opts.Keyset is not { } keyset)
        {
            throw new ArgumentException(
                "ListKeysetAsync reads QueryOptions.Keyset, which is not set: set it with WithKeyset(KeysetPagination<T>.OrderBy(...)).",
                nameof(opts));
        }

        if (opts.OrderBy is not null || opts.Pagination is not null)
        {
            throw new ArgumentException(
                "A keyset owns the ordering and the page: these options also set " +
                (opts.OrderBy is not null ? "OrderBy" : "Pagination") + ". Remove it; the keyset's sort keys order the page.",
                nameof(opts));
        }

        return keyset;
    }

    /// <summary>Refuses options with a keyset in a method that does not read it.</summary>
    /// <exception cref="ArgumentException"><paramref name="opts"/> sets a keyset.</exception>
    internal static void EnsureNone<TAggregate>(QueryOptions<TAggregate> opts, string method)
        where TAggregate : class, IAggregateRoot
    {
        if (opts.Keyset is not null)
        {
            throw new ArgumentException(
                $"{method} does not read QueryOptions.Keyset: call ListKeysetAsync, or clear it with WithKeyset(null).",
                nameof(opts));
        }
    }
}
