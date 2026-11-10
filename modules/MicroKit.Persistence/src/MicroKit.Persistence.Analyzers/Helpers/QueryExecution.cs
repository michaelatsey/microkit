namespace MicroKit.Persistence.Analyzers.Helpers;

/// <summary>
/// Whether a call runs a query, as opposed to composing one. Composition returns a query
/// (<c>Where</c>, <c>Include</c>, <c>OrderBy</c>, <c>AsSplitQuery</c>, <c>Select</c>…) and runs nothing.
/// </summary>
internal static class QueryExecution
{
    private static readonly HashSet<string> Materializers = new(StringComparer.Ordinal)
    {
        "ToList", "ToArray", "ToDictionary", "ToHashSet", "ToLookup",
    };

    /// <summary>
    /// <paramref name="invocation"/> executes a LINQ query: a <c>Queryable</c> operator that returns no query; an
    /// <c>Enumerable</c> materializer or scalar over a query; an EF Core async executor or <c>Load</c>.
    /// EF Core's bulk <c>Execute*</c> writes are not reads, and <c>AsAsyncEnumerable</c> only defers.
    /// </summary>
    public static bool ExecutesQuery(IInvocationOperation invocation, QuerySymbols symbols)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;

        if (QuerySymbols.IsDeclaredBy(method, symbols.Queryable))
            return !symbols.IsQueryable(method.ReturnType);

        if (QuerySymbols.IsDeclaredBy(method, symbols.Enumerable))
        {
            if (!ReadsAQuery(invocation, symbols))
                return false;
            return Materializers.Contains(method.Name) || !symbols.IsCollection(method.ReturnType);
        }

        if (QuerySymbols.IsDeclaredBy(method, symbols.EfQueryableExtensions))
        {
            var name = method.Name;
            if (name == "Load")
                return true;
            return name.EndsWith("Async", StringComparison.Ordinal) &&
                   name != "AsAsyncEnumerable" &&
                   !name.StartsWith("Execute", StringComparison.Ordinal);
        }

        return false;
    }

    // An Enumerable call reads a query when its receiver is one, or is AsEnumerable() of one.
    private static bool ReadsAQuery(IInvocationOperation invocation, QuerySymbols symbols)
    {
        var receiver = QueryChain.ReceiverOf(invocation);
        if (receiver is null)
            return false;
        if (symbols.IsQueryable(receiver.Type))
            return !IsLinqToObjects(receiver, symbols);
        return receiver is IInvocationOperation { TargetMethod.Name: "AsEnumerable" } asEnumerable &&
               QueryChain.ReceiverOf(asEnumerable) is { } source &&
               symbols.IsQueryable(source.Type) &&
               !IsLinqToObjects(source, symbols);
    }

    /// <summary>The query is rooted at <c>Queryable.AsQueryable(…)</c> over objects in memory.</summary>
    public static bool IsLinqToObjects(IOperation query, QuerySymbols symbols)
    {
        var chain = QueryChain.Flatten(query);
        var root = QueryChain.QueryRootIndex(chain, symbols);
        return root >= 0 &&
               chain[root] is IInvocationOperation invocation &&
               QuerySymbols.Is(invocation.TargetMethod, symbols.Queryable, "AsQueryable");
    }
}
