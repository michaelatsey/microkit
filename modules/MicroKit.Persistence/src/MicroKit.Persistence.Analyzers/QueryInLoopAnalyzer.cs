using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP007: a query runs on each iteration of a <c>foreach</c>, <c>await foreach</c> or <c>for</c> loop — the N+1
/// shape. Load what the loop needs in one query before it (ADR-014).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryInLoopAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP007";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "A query runs on each iteration of a loop",
        messageFormat: "'{0}' runs a query on each iteration of the loop; load what the loop needs in one query before it (Contains over the keys, or an Include)",
        category: DiagnosticCategories.Performance,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A query inside a foreach or for loop costs one database round trip per element. Load the data once "
                   + "before the loop. Composing a query in a loop (Where, Include) runs nothing and is not reported; neither "
                   + "are while and do loops or a for loop without a condition (the batching, polling and retry idioms), nor a page "
                   + "walk with ListKeysetAsync or ListPagedAsync.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = PersistenceSymbolHelper.ResolveQuerySymbols(start.Compilation);
            var entityFrameworkCore = symbols.HasEntityFrameworkCore;
            var repositories = symbols.HasRepositories;
            if (!entityFrameworkCore && !repositories)
                return;

            start.RegisterOperationAction(operation =>
            {
                var invocation = (IInvocationOperation)operation.Operation;
                var queries = (entityFrameworkCore && RunsAnEfQuery(invocation, symbols)) ||
                              (repositories && ReadsARepository(invocation.TargetMethod, symbols));
                if (queries && InsideALoop(invocation))
                {
                    operation.ReportDiagnostic(
                        Diagnostic.Create(Rule, QueryChain.NameLocation(invocation), invocation.TargetMethod.Name));
                }
            }, OperationKind.Invocation);

            if (entityFrameworkCore)
            {
                start.RegisterOperationAction(operation =>
                {
                    // A nested foreach over a query enumerates it once per iteration of the outer loop.
                    if (operation.Operation is not IForEachLoopOperation loop)
                        return;
                    if (EnumeratesAQuery(loop.Collection, symbols) && InsideALoop(loop))
                    {
                        operation.ReportDiagnostic(
                            Diagnostic.Create(Rule, loop.Collection.Syntax.GetLocation(), loop.Collection.Syntax.ToString()));
                    }
                }, OperationKind.Loop);
            }
        });
    }

    // Rules 1–6: LINQ executors over a query, EF Core's async executors, Find, explicit loading.
    private static bool RunsAnEfQuery(IInvocationOperation invocation, QuerySymbols symbols)
    {
        var method = invocation.TargetMethod;
        if (QueryExecution.ExecutesQuery(invocation, symbols))
            return QueryChain.ReceiverOf(invocation) is not { } receiver || !QueryExecution.IsLinqToObjects(receiver, symbols);

        if (method.Name is "Find" or "FindAsync")
            return symbols.IsDbSet(method.ContainingType) || QuerySymbols.DerivesFrom(method.ContainingType, symbols.DbContext);

        return method.Name is "Load" or "LoadAsync" && QuerySymbols.DerivesFrom(method.ContainingType, symbols.NavigationEntry);
    }

    // Rule 7: an awaitable method of a MicroKit repository, other than the staging verbs and the paged reads,
    // which a page walk calls once per page by design.
    private static bool ReadsARepository(IMethodSymbol method, QuerySymbols symbols)
    {
        var type = method.ContainingType;
        if (!QuerySymbols.IsOrImplements(type, symbols.Repository) && !QuerySymbols.IsOrImplements(type, symbols.ReadRepository))
            return false;

        var name = method.Name;
        if (name.StartsWith("Add", StringComparison.Ordinal) ||
            name.StartsWith("Delete", StringComparison.Ordinal) ||
            name.StartsWith("Remove", StringComparison.Ordinal) ||
            name is "ListKeysetAsync" or "ListPagedAsync")
        {
            return false;
        }

        return method.ReturnType is INamedTypeSymbol { Name: "Task" or "ValueTask" } returned &&
               returned.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    // The foreach collection is a query, possibly through AsAsyncEnumerable(), WithCancellation(…), ConfigureAwait(…).
    private static bool EnumeratesAQuery(IOperation collection, QuerySymbols symbols)
    {
        var current = QueryChain.Strip(collection);
        while (!symbols.IsQueryable(current.Type) &&
               current is IInvocationOperation { TargetMethod.Name: "AsAsyncEnumerable" or "WithCancellation" or "ConfigureAwait" or "AsEnumerable" } step &&
               QueryChain.ReceiverOf(step) is { } receiver)
        {
            current = receiver;
        }

        return symbols.IsQueryable(current.Type) && !QueryExecution.IsLinqToObjects(current, symbols);
    }

    // Walks out to the nearest foreach or for loop whose iterations run the operation: its body, a for loop's
    // condition and incrementors. A foreach collection and a for initializer run once for that loop, so the
    // search goes on outward. while and do loops, and a for loop without a condition, are the batching and retry
    // idioms and do not count. A lambda or a local
    // function stops the search: when it runs is unknown.
    private static bool InsideALoop(IOperation operation)
    {
        var child = operation;
        for (var parent = operation.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    return false;
                case IForEachLoopOperation forEach when forEach.Collection != child:
                    return true;
                case IForLoopOperation { Condition: not null } forLoop when !forLoop.Before.Contains(child):
                    return true;
            }
        }

        return false;
    }
}
