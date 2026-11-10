using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP006: one query includes two or more collection navigations without a split decision
/// (<c>AsSplitQuery()</c> or <c>AsSingleQuery()</c>). A suggestion, with no code fix: splitting changes the read's
/// consistency (ADR-014).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionIncludeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP006";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "A query includes two or more collections without a split decision",
        messageFormat: "This query loads the collections '{0}' and '{1}' in one query, repeating the rows of one for each row of the other; call AsSplitQuery() (the reads are then not one snapshot) or AsSingleQuery() to keep one query",
        category: DiagnosticCategories.Performance,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Two collection navigations loaded by one SQL query multiply their rows: the result carries one row per "
                   + "combination. AsSplitQuery() loads each collection in its own query, which is no longer one snapshot under "
                   + "concurrent writes; AsSingleQuery() keeps the single query on purpose. On MicroKit's QueryOptions, call "
                   + "AsSplitQuery() on the options. One collection is a single LEFT JOIN and is not reported.",
        helpLinkUri: HelpLinks.For(DiagnosticId));

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
            if (!symbols.HasRelational)
                return; // no EF Core, or a provider without split queries: the message would name an absent API

            start.RegisterOperationAction(operation => AnalyzeChainEnd(operation, symbols), OperationKind.Invocation);
        });
    }

    // Each query chain is analyzed once, from its outermost call.
    private static void AnalyzeChainEnd(OperationAnalysisContext context, QuerySymbols symbols)
    {
        var end = (IInvocationOperation)context.Operation;
        if (QueryChain.NextOf(end) is not null || !HasALambdaInclude(end, symbols))
            return;

        var chain = QueryChain.Flatten(end);
        var rootIndex = QueryChain.QueryRootIndex(chain, symbols);
        if (rootIndex < 0)
            return;

        var collections = new List<string>();
        var paths = new Dictionary<IOperation, string>();
        IInvocationOperation? second = null;
        var decided = false;
        var ignored = false;
        for (var i = rootIndex + 1; i < chain.Count; i++)
        {
            if (chain[i] is not IInvocationOperation call)
                continue;
            var method = call.TargetMethod;
            if (QuerySymbols.Is(method, symbols.RelationalQueryableExtensions, "AsSplitQuery") ||
                QuerySymbols.Is(method, symbols.RelationalQueryableExtensions, "AsSingleQuery"))
            {
                decided = true;
                continue;
            }

            if (IgnoresTheIncludes(call, symbols))
            {
                ignored = true;
                continue;
            }

            if (!IncludesANavigation(call, symbols, out var navigation, out var property))
                continue;

            // A ThenInclude continues the path of the include it follows.
            var parent = method.Name == "ThenInclude" && paths.TryGetValue(chain[i - 1], out var previous) ? previous + "." : "";
            var path = parent + navigation;
            paths[call] = path;
            if (!symbols.IsCollection(property) || collections.Contains(path))
                continue;
            collections.Add(path);
            if (collections.Count == 2)
                second = call;
        }

        if (second is null || decided || ignored || !IsWholeQuery(chain[rootIndex], end, symbols))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, QueryChain.NameLocation(second), collections[0], collections[1]));
    }

    // Most chain ends hold no include: walk the receivers without allocating, and bail out early.
    private static bool HasALambdaInclude(IInvocationOperation end, QuerySymbols symbols)
    {
        for (IOperation? current = end; current is IInvocationOperation call; current = QueryChain.ReceiverOf(call))
        {
            if (call.TargetMethod.Name is "Include" or "ThenInclude" &&
                QuerySymbols.IsDeclaredBy(call.TargetMethod, symbols.EfQueryableExtensions))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly HashSet<string> Projections = new(StringComparer.Ordinal)
    {
        "Select", "SelectMany", "GroupBy", "Join", "GroupJoin",
    };

    private static readonly HashSet<string> Scalars = new(StringComparer.Ordinal)
    {
        "Count", "LongCount", "Any", "All", "Sum", "Min", "Max", "Average", "Contains", "Aggregate", "SequenceEqual",
    };

    // EF Core ignores the includes of a query that returns no entity: a projection, or a scalar such as a count.
    private static bool IgnoresTheIncludes(IInvocationOperation call, QuerySymbols symbols)
    {
        var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
        if (QuerySymbols.IsDeclaredBy(method, symbols.Queryable) && Projections.Contains(method.Name))
            return true;
        if (!QueryExecution.ExecutesQuery(call, symbols))
            return false;
        var name = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name.Substring(0, method.Name.Length - 5) : method.Name;
        return Scalars.Contains(name);
    }

    // Include(o => o.Nav) or ThenInclude(x => x.Nav), with the navigation's name and type. Include(string) names
    // no navigation the analyzer can see.
    private static bool IncludesANavigation(IInvocationOperation call, QuerySymbols symbols, out string navigation, out ITypeSymbol? type)
    {
        navigation = "";
        type = null;
        var method = call.TargetMethod;
        int typeArgument;
        if (QuerySymbols.Is(method, symbols.EfQueryableExtensions, "Include") && method.TypeArguments.Length == 2)
            typeArgument = 1;
        else if (QuerySymbols.Is(method, symbols.EfQueryableExtensions, "ThenInclude") && method.TypeArguments.Length == 3)
            typeArgument = 2;
        else
            return false;

        type = method.TypeArguments[typeArgument];
        navigation = NavigationName(call) ?? LambdaText(call);
        return true;
    }

    // A lambda that is not a plain property read (a cast) is named by its text, so two of them do not merge.
    private static string LambdaText(IInvocationOperation call)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter?.Ordinal == 1)
                return argument.Value.Syntax.ToString();
        }

        return call.Syntax.ToString();
    }

    // The name of the navigation the include's lambda reads: o => o.Lines, or o => o.Lines.Where(…) (filtered).
    private static string? NavigationName(IInvocationOperation call)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter?.Ordinal != 1)
                continue;
            var lambda = QueryChain.Strip(argument.Value);
            if (lambda is IDelegateCreationOperation creation)
                lambda = creation.Target;
            if (lambda is not IAnonymousFunctionOperation function ||
                function.Body.Operations.FirstOrDefault() is not IReturnOperation { ReturnedValue: { } value })
            {
                return null;
            }

            var current = QueryChain.Strip(value);
            while (current is IInvocationOperation filter && QueryChain.ReceiverOf(filter) is { } receiver)
                current = receiver;
            return current is IPropertyReferenceOperation property ? property.Property.Name : null;
        }

        return null;
    }

    // The analyzer reports only a query it sees whole: an EF chain from a DbSet that it sees executed, or the
    // includes of a QueryOptions whose split it can read.
    private static bool IsWholeQuery(IOperation root, IInvocationOperation end, QuerySymbols symbols)
    {
        if (symbols.IsDbSet(root.Type))
        {
            foreach (var operation in QueryChain.Flatten(end))
            {
                if (operation is IInvocationOperation call && QueryExecution.ExecutesQuery(call, symbols))
                    return true;
            }

            return QueryChain.IsEnumeratedByLoop(end, out _);
        }

        return root is IParameterReferenceOperation parameter && IncludesOfOptionsWithoutSplit(parameter, end, symbols);
    }

    // q => q.Include(…)… passed to WithIncludes, or assigned to Includes in an initializer or a with expression.
    private static bool IncludesOfOptionsWithoutSplit(IParameterReferenceOperation parameter, IInvocationOperation end, QuerySymbols symbols)
    {
        if (QueryChain.ParentOf(end, out _) is not IReturnOperation)
            return false;

        IOperation? function = parameter.Parent;
        while (function is not null && !(function is IAnonymousFunctionOperation lambda && lambda.Symbol.Parameters.Contains(parameter.Parameter, SymbolEqualityComparer.Default)))
            function = function.Parent;
        if (function is null)
            return false;

        var holder = QueryChain.ParentOf(function, out var child);
        if (holder is IDelegateCreationOperation)
            holder = QueryChain.ParentOf(holder, out child);

        if (holder is IArgumentOperation argument &&
            argument.Parent is IInvocationOperation withIncludes &&
            QuerySymbols.Is(withIncludes.TargetMethod, symbols.QueryOptionsExtensions, "WithIncludes"))
        {
            return !OptionsSplitOrDropIncludes(withIncludes, symbols);
        }

        if (holder is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.Name: "Includes" } target } assignment &&
            QuerySymbols.DerivesFrom(target.Property.ContainingType, symbols.QueryOptions) &&
            assignment.Parent is IObjectOrCollectionInitializerOperation initializer &&
            initializer.Parent is { } options)
        {
            return !OptionsSplitOrDropIncludes(options, symbols);
        }

        return false;
    }

    // Walks the options chain both ways: AsSplitQuery(), or AsSplitQueryEnabled = true in an initializer or a with,
    // settles the split; options passed inline to CountAsync, AnyAsync or a selector overload drop the includes.
    private static bool OptionsSplitOrDropIncludes(IOperation options, QuerySymbols symbols)
    {
        for (var current = options; current is not null;)
        {
            if (SplitsOptions(current, symbols))
                return true;
            current = current switch
            {
                IInvocationOperation invocation => QueryChain.ReceiverOf(invocation),
                IWithOperation with => QueryChain.Strip(with.Operand),
                _ => null,
            };
        }

        var top = options;
        while (true)
        {
            if (SplitsOptions(top, symbols))
                return true;
            if (QueryChain.NextOf(top) is { } next)
            {
                top = next;
                continue;
            }

            var parent = QueryChain.ParentOf(top, out var child);
            if (parent is IWithOperation with && with.Operand == child)
            {
                top = with;
                continue;
            }

            return parent is IArgumentOperation { Parent: IInvocationOperation reader } && DropsIncludes(reader.TargetMethod, symbols);
        }
    }

    private static bool SplitsOptions(IOperation operation, QuerySymbols symbols)
    {
        if (operation is IInvocationOperation invocation)
            return QuerySymbols.Is(invocation.TargetMethod, symbols.QueryOptionsExtensions, "AsSplitQuery");

        var initializer = operation switch
        {
            IObjectCreationOperation creation => creation.Initializer,
            IWithOperation with => with.Initializer,
            _ => null,
        };
        if (initializer is null)
            return false;
        foreach (var member in initializer.Initializers)
        {
            if (member is ISimpleAssignmentOperation
                {
                    Target: IPropertyReferenceOperation { Property.Name: "AsSplitQueryEnabled" },
                    Value: ILiteralOperation { ConstantValue: { HasValue: true, Value: true } },
                })
            {
                return true;
            }
        }

        return false;
    }

    // CountAsync and AnyAsync drop the includes, and so does every projection (an overload with a selector).
    private static bool DropsIncludes(IMethodSymbol method, QuerySymbols symbols)
    {
        if (!QuerySymbols.IsOrImplements(method.ContainingType, symbols.ReadRepository))
            return false;
        if (method.Name is "CountAsync" or "AnyAsync")
            return true;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Name == "selector")
                return true;
        }

        return false;
    }
}
