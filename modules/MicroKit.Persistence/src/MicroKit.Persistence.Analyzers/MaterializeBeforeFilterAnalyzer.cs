using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP008: a whole EF Core query is materialized (<c>ToList</c>, <c>ToArray</c>, their async forms), then filtered in
/// memory by a predicate that plainly translates: every row crosses the wire to be discarded. No code fix: moving the
/// filter into SQL can change its result (ADR-014).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaterializeBeforeFilterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP008";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "A query is materialized before it is filtered",
        messageFormat: "'{0}' loads every row before '{1}' filters them in memory; move the filter before '{0}' so the database applies it",
        category: DiagnosticCategories.Performance,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Filtering after ToList() or ToArray() reads every row of the query, then discards most of them. Moving the "
                   + "filter before the materialization lets the database apply it; check that the database compares the same "
                   + "way (collation, precision) and that tracked entities' unsaved values do not matter. Reported only for a "
                   + "query from a DbSet the filter commutes with, and a predicate over mapped scalar properties; AsEnumerable() "
                   + "states a deliberate in-memory filter.",
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
            if (!symbols.HasEntityFrameworkCore || symbols.Enumerable is null)
                return;

            start.RegisterOperationAction(operation => AnalyzeFilter(operation, symbols), OperationKind.Invocation);
        });
    }

    private static readonly HashSet<string> Filters = new(StringComparer.Ordinal)
    {
        "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Any", "All", "Count", "LongCount",
    };

    // Operators a filter moves past unchanged: it keeps or drops the same rows before or after them.
    private static readonly HashSet<string> CommutingQueryable = new(StringComparer.Ordinal)
    {
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
    };

    private static readonly HashSet<string> CommutingEntityFrameworkCore = new(StringComparer.Ordinal)
    {
        "Include", "ThenInclude", "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsTracking",
        "IgnoreQueryFilters", "TagWith", "TagWithCallSite",
    };

    private static void AnalyzeFilter(OperationAnalysisContext context, QuerySymbols symbols)
    {
        var filter = (IInvocationOperation)context.Operation;
        var method = filter.TargetMethod.ReducedFrom ?? filter.TargetMethod;
        if (!QuerySymbols.IsDeclaredBy(method, symbols.Enumerable) || !Filters.Contains(method.Name) || method.Parameters.Length != 2)
            return; // not a predicate filter; Where((x, i) => …) fails the single-parameter predicate rule below

        if (QueryChain.ReceiverOf(filter) is not { } materialized ||
            Materializer(materialized, symbols) is not { } materializer ||
            QueryChain.ReceiverOf(materializer) is not { } query ||
            !symbols.IsQueryable(query.Type))
        {
            return;
        }

        if (!IsWholeCommutingQuery(query, symbols, out var ordered) || (method.Name.StartsWith("Last", StringComparison.Ordinal) && !ordered))
            return; // EF Core refuses Last without an ordering

        if (!PredicatePlainlyTranslates(filter, symbols))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, QueryChain.NameLocation(materializer), materializer.TargetMethod.Name, method.Name));
    }

    // Enumerable.ToList()/ToArray() over a query, or an awaited ToListAsync()/ToArrayAsync().
    private static IInvocationOperation? Materializer(IOperation operation, QuerySymbols symbols)
    {
        var awaited = QueryChain.Strip(operation) is IAwaitOperation;
        if (QueryChain.UnwrapAwait(operation) is not IInvocationOperation call)
            return null;
        var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
        if (awaited)
            return QuerySymbols.IsDeclaredBy(method, symbols.EfQueryableExtensions) && method.Name is "ToListAsync" or "ToArrayAsync" ? call : null;
        return QuerySymbols.IsDeclaredBy(method, symbols.Enumerable) && method.Name is "ToList" or "ToArray" ? call : null;
    }

    // Rooted at a DbSet (or Set<T>()), with only filter-commuting operators between the root and the materializer.
    private static bool IsWholeCommutingQuery(IOperation query, QuerySymbols symbols, out bool ordered)
    {
        ordered = false;
        var chain = QueryChain.Flatten(query);
        var root = QueryChain.QueryRootIndex(chain, symbols);
        if (root < 0 || !symbols.IsDbSet(chain[root].Type))
            return false;

        for (var i = root + 1; i < chain.Count; i++)
        {
            if (chain[i] is not IInvocationOperation call)
                return false;
            var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
            var commutes =
                (QuerySymbols.IsDeclaredBy(method, symbols.Queryable) && CommutingQueryable.Contains(method.Name)) ||
                (QuerySymbols.IsDeclaredBy(method, symbols.EfQueryableExtensions) && CommutingEntityFrameworkCore.Contains(method.Name)) ||
                (QuerySymbols.IsDeclaredBy(method, symbols.RelationalQueryableExtensions) && method.Name is "AsSplitQuery" or "AsSingleQuery");
            if (!commutes)
                return false;
            ordered |= method.Name.StartsWith("OrderBy", StringComparison.Ordinal);
        }

        return true;
    }

    // An expression-bodied lambda whose every part reading the element is a mapped scalar property, a literal, a
    // built-in or scalar operator, or a call EF Core translates; the rest is captured and sent as parameters.
    private static bool PredicatePlainlyTranslates(IInvocationOperation filter, QuerySymbols symbols)
    {
        IOperation? predicate = null;
        foreach (var argument in filter.Arguments)
        {
            if (argument.Parameter?.Ordinal == 1)
                predicate = QueryChain.Strip(argument.Value);
        }

        if (predicate is IDelegateCreationOperation creation)
            predicate = creation.Target;
        if (predicate is not IAnonymousFunctionOperation lambda ||
            lambda.Syntax is not LambdaExpressionSyntax { ExpressionBody: not null } ||
            lambda.Symbol.Parameters.Length != 1 ||
            lambda.Body.Operations.Length != 1 ||
            lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } body })
        {
            return false; // a method group is opaque; a block body cannot become an expression tree
        }

        return new Translation(lambda.Symbol.Parameters[0], symbols).Translates(body);
    }

    private sealed class Translation(IParameterSymbol element, QuerySymbols symbols)
    {
        public bool Translates(IOperation operation)
        {
            if (!ReadsTheElement(operation))
                return IsCaptured(operation);

            switch (operation)
            {
                case IPropertyReferenceOperation property:
                    return property.Instance is IParameterReferenceOperation parameter &&
                           SymbolEqualityComparer.Default.Equals(parameter.Parameter, element) &&
                           IsMappedScalar(property.Property);
                case IBinaryOperation binary:
                    return (binary.OperatorMethod is null || IsScalar(binary.OperatorMethod.ContainingType)) &&
                           Translates(binary.LeftOperand) && Translates(binary.RightOperand);
                case IUnaryOperation unary:
                    return unary.OperatorMethod is null && Translates(unary.Operand);
                case IConversionOperation conversion:
                    return !conversion.Conversion.IsUserDefined && Translates(conversion.Operand);
                case ICoalesceOperation coalesce:
                    return Translates(coalesce.Value) && Translates(coalesce.WhenNull);
                case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                    return Translates(conditional.Condition) && Translates(conditional.WhenTrue) && Translates(whenFalse);
                case IInvocationOperation invocation:
                    return IsTranslatedCall(invocation);
                default:
                    return false; // a pattern, ?., a switch or throw expression, a call on the element…
            }
        }

        // string.Contains/StartsWith/EndsWith(string); Contains over a captured collection of an element's scalar.
        private bool IsTranslatedCall(IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            if (!ExplicitArguments(invocation))
                return false;

            if (method.ContainingType.SpecialType == SpecialType.System_String &&
                method.Name is "Contains" or "StartsWith" or "EndsWith" &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String)
            {
                return invocation.Instance is { } instance && Translates(instance) && Translates(invocation.Arguments[0].Value);
            }

            if (method.Name != "Contains")
                return false;

            // Enumerable.Contains(source, value), MemoryExtensions.Contains(span, value) (C# 14 binds arrays to it),
            // or an instance Contains(value) of a captured collection.
            IOperation? source;
            IOperation? value;
            if (method.IsStatic &&
                (QuerySymbols.IsDeclaredBy(method, symbols.Enumerable) || QuerySymbols.IsDeclaredBy(method, symbols.MemoryExtensions)) &&
                method.Parameters.Length == 2)
            {
                source = ArgumentAt(invocation, 0);
                value = ArgumentAt(invocation, 1);
            }
            else if (!method.IsStatic && method.Parameters.Length == 1 && symbols.IsCollection(method.ContainingType))
            {
                source = invocation.Instance;
                value = ArgumentAt(invocation, 0);
            }
            else
            {
                return false;
            }

            return source is not null && value is not null && !ReadsTheElement(source) && IsCaptured(source) && Translates(value);
        }

        private static IOperation? ArgumentAt(IInvocationOperation invocation, int ordinal)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter?.Ordinal == ordinal)
                    return argument.Value;
            }

            return null;
        }

        private bool ReadsTheElement(IOperation operation)
        {
            foreach (var descendant in operation.DescendantsAndSelf())
            {
                if (descendant is IParameterReferenceOperation parameter &&
                    SymbolEqualityComparer.Default.Equals(parameter.Parameter, element))
                {
                    return true;
                }
            }

            return false;
        }

        // A part that does not read the element is evaluated by EF Core and sent as a parameter. It must still be
        // something an expression tree can hold: no pattern, ?., switch, throw, lambda or omitted optional argument.
        private static bool IsCaptured(IOperation operation)
        {
            foreach (var descendant in operation.DescendantsAndSelf())
            {
                switch (descendant)
                {
                    case ILiteralOperation or ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation
                        or IInstanceReferenceOperation or IConversionOperation or IBinaryOperation or IUnaryOperation
                        or IDefaultValueOperation or ITypeOfOperation or IArrayCreationOperation or IArrayInitializerOperation:
                        continue;
                    case IPropertyReferenceOperation { Arguments.Length: 0 }:
                        continue;
                    case IInvocationOperation invocation when ExplicitArguments(invocation):
                        continue;
                    case IArgumentOperation { ArgumentKind: ArgumentKind.Explicit, Parameter.RefKind: RefKind.None }:
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool ExplicitArguments(IInvocationOperation invocation)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit || argument.Parameter?.RefKind is not RefKind.None)
                    return false;
            }

            return true;
        }

        // EF Core maps a property with a public getter and a setter by convention; [NotMapped], a non-public or a
        // get-only (computed) one may not be.
        private bool IsMappedScalar(IPropertySymbol property)
        {
            if (property.GetMethod?.DeclaredAccessibility != Accessibility.Public || property.SetMethod is null ||
                !property.Parameters.IsEmpty || !IsScalar(property.Type))
            {
                return false;
            }
            foreach (var attribute in property.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.NotMapped))
                    return false;
            }

            return true;
        }

        private static bool IsScalar(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                type = nullable.TypeArguments[0];
            if (type.TypeKind == TypeKind.Enum)
                return true;
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_String:
                case SpecialType.System_DateTime:
                    return true;
            }

            return type.ContainingNamespace?.ToDisplayString() == "System" &&
                   type.Name is "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" or "Guid";
        }
    }
}
