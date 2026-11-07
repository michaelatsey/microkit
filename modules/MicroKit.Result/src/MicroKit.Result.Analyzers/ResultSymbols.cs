namespace MicroKit.Result.Analyzers;

/// <summary>
/// The MicroKit.Result types the analyzers recognize, resolved by metadata name so the analyzers do not reference
/// the library.
/// </summary>
internal sealed class ResultSymbols
{
    private ResultSymbols(
        INamedTypeSymbol? result,
        INamedTypeSymbol? resultOfT,
        INamedTypeSymbol? task,
        INamedTypeSymbol? valueTask,
        INamedTypeSymbol? extensions,
        INamedTypeSymbol? asyncExtensions)
    {
        Result = result;
        ResultOfT = resultOfT;
        TaskOfT = task;
        ValueTaskOfT = valueTask;
        Extensions = extensions;
        AsyncExtensions = asyncExtensions;
    }

    public INamedTypeSymbol? Result { get; }

    public INamedTypeSymbol? ResultOfT { get; }

    public INamedTypeSymbol? TaskOfT { get; }

    public INamedTypeSymbol? ValueTaskOfT { get; }

    public INamedTypeSymbol? Extensions { get; }

    public INamedTypeSymbol? AsyncExtensions { get; }

    public static ResultSymbols From(Compilation compilation) => new(
        compilation.GetTypeByMetadataName("MicroKit.Result.Result"),
        compilation.GetTypeByMetadataName("MicroKit.Result.Result`1"),
        compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
        compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"),
        compilation.GetTypeByMetadataName("MicroKit.Result.ResultExtensions"),
        compilation.GetTypeByMetadataName("MicroKit.Result.AsyncResultExtensions"));

    /// <summary>A <c>Result</c> or <c>Result&lt;T&gt;</c>, or a nullable one (from a conditional access).</summary>
    public bool IsResult(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type is INamedTypeSymbol named
            && ((Result is not null && SymbolEqualityComparer.Default.Equals(named, Result))
                || (ResultOfT is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, ResultOfT)));
    }

    /// <summary>A <c>Task</c> or <c>ValueTask</c> of a <c>Result</c> or <c>Result&lt;T&gt;</c>, nullable included.</summary>
    public bool IsTaskOfResult(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type is INamedTypeSymbol { IsGenericType: true } named
            && ((TaskOfT is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, TaskOfT))
                || (ValueTaskOfT is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, ValueTaskOfT)))
            && IsResult(named.TypeArguments[0]);
    }

    /// <summary>A side-effect method of MicroKit.Result itself, not a user method of the same name.</summary>
    public bool IsLibraryExtension(IMethodSymbol method)
    {
        var owner = (method.ReducedFrom ?? method).ContainingType;
        return (Extensions is not null && SymbolEqualityComparer.Default.Equals(owner, Extensions))
            || (AsyncExtensions is not null && SymbolEqualityComparer.Default.Equals(owner, AsyncExtensions));
    }
}
