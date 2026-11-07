namespace MicroKit.Result.Analyzers;

/// <summary>
/// MKR001: a <c>Result</c> or <c>Result&lt;T&gt;</c> returned by a call, or by an awaited task, and then discarded.
/// </summary>
/// <remarks>
/// A discarded result loses its failure: the error is never handled. Reported: an expression statement whose value
/// is a result (a call, an <c>await</c>, a conditional access <c>x?.Save()</c>), and a <c>Task</c> of a result
/// or a <c>ValueTask</c> of a result discarded where CS4014 does not catch it: outside an async method or lambda, or
/// through a conditional access or a property inside one. Not reported: an explicit discard (<c>_ = …;</c>) or any assignment;
/// <c>Tap</c>, <c>TapError</c> and <c>Finally</c> (and their <c>…Async</c> forms, awaited, with or without
/// <c>ConfigureAwait</c>), alone, chained or through a conditional access (<c>held?.Tap(…)</c>), on a result already held in a local, a parameter, a field, an
/// auto-property, an indexer or an array element: the side effect is the point, and the result is still held. A computed
/// property (a getter with a body) builds a new result at each read: a side effect on it is reported, even when the body
/// only returns a field (<c>Current =&gt; _current</c>), as is an abstract or interface property, whose storage is unknown.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedResultAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKR001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Result is discarded",
        messageFormat: "The {0} returned here is discarded: its failure is never handled",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Result or Result<T> that is returned and then ignored loses its failure. Assign it and handle "
                   + "both rails (Match, TryGetValue, an early return on IsFailure), return it, await it, or discard it "
                   + "explicitly with '_ =' when ignoring the outcome is intended.",
        helpLinkUri: "https://github.com/michaelatsey/microkit/blob/main/modules/MicroKit.Result/docs/guides/usage.md#mkr001-a-result-is-discarded");

    private static readonly ImmutableHashSet<string> SideEffectMethods =
        ImmutableHashSet.Create("Tap", "TapError", "Finally", "TapAsync", "TapErrorAsync", "FinallyAsync");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = ResultSymbols.From(start.Compilation);
            if (symbols.Result is null && symbols.ResultOfT is null)
                return;

            start.RegisterOperationAction(c => Analyze(c, symbols), OperationKind.ExpressionStatement);
        });
    }

    private static void Analyze(OperationAnalysisContext context, ResultSymbols symbols)
    {
        var statement = (IExpressionStatementOperation)context.Operation;
        var operation = statement.Operation;

        // An assignment, an explicit discard (_ = …) included, keeps or states what happens to the result.
        if (operation is IAssignmentOperation)
            return;

        var isResult = symbols.IsResult(operation.Type);
        if (!isResult && !IsUnobservedTaskOfResult(operation, statement, context.ContainingSymbol, symbols))
            return;

        if (isResult && IsSideEffectOnHeldResult(operation, symbols))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            operation.Syntax.GetLocation(),
            operation.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))); // Safe: IsResult/IsTaskOfResult checked the type
    }

    // A Task or ValueTask of a result is reported unless CS4014 covers it: a direct call inside an async method,
    // lambda or local function. A conditional access or a property read is not a call, so CS4014 misses it.
    private static bool IsUnobservedTaskOfResult(
        IOperation operation, IOperation statement, ISymbol containingSymbol, ResultSymbols symbols) =>
        symbols.IsTaskOfResult(operation.Type)
        && !(operation is IInvocationOperation && IsInAsyncContext(statement, containingSymbol));

    private static bool IsInAsyncContext(IOperation operation, ISymbol containingSymbol)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation lambda:
                    return lambda.Symbol.IsAsync;
                case ILocalFunctionOperation local:
                    return local.Symbol.IsAsync;
            }
        }

        return containingSymbol is IMethodSymbol { IsAsync: true };
    }

    private static bool IsSideEffectOnHeldResult(IOperation operation, ResultSymbols symbols)
    {
        var current = operation is IAwaitOperation awaited ? awaited.Operation : operation;
        if (current is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } configured })
            current = configured;

        // held?.Tap(a): the receiver inside WhenNotNull is the conditional access's own operand.
        IOperation? conditionalOperand = null;
        if (current is IConditionalAccessOperation conditional)
        {
            conditionalOperand = conditional.Operation;
            current = conditional.WhenNotNull;
        }

        // held.Tap(a).TapError(b): walk down the chain while each step is a side effect.
        while (current is IInvocationOperation invocation
               && SideEffectMethods.Contains(invocation.TargetMethod.Name)
               && symbols.IsLibraryExtension(invocation.TargetMethod))
        {
            var receiver = invocation.Instance
                ?? (invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0
                    ? invocation.Arguments[0].Value
                    : null);

            while (receiver is IConversionOperation conversion)
                receiver = conversion.Operand;

            if (receiver is IConditionalAccessInstanceOperation && conditionalOperand is not null)
            {
                receiver = conditionalOperand;
                while (receiver is IConversionOperation conversion)
                    receiver = conversion.Operand;
            }

            if (receiver is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation
                or IArrayElementReferenceOperation
                || (receiver is IPropertyReferenceOperation { Property: var property } && IsStoredProperty(property)))
            {
                return true;
            }

            current = receiver;
        }

        return false;
    }

    // A computed property builds a new result at each read: the one its side effect returns is then discarded. An
    // auto-property stores its result, and an indexer is element access, like an array's.
    private static bool IsStoredProperty(IPropertySymbol property)
    {
        // The value of a Nullable<T> is the stored struct itself: held?.Value.Tap(…), cached.Value.Tap(…).
        if (property.IsIndexer
            || (property.Name == "Value" && property.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return true;
        }

        // Source, and metadata when private members are imported: the compiler's backing field.
        foreach (var member in property.OriginalDefinition.ContainingType.GetMembers())
        {
            if (member is IFieldSymbol { AssociatedSymbol: { } associated }
                && SymbolEqualityComparer.Default.Equals(associated.OriginalDefinition, property.OriginalDefinition))
                return true;
        }

        // Metadata: the compiler marks an auto-property's accessors [CompilerGenerated].
        return property.GetMethod?.GetAttributes().Any(a => a.AttributeClass is
        {
            Name: "CompilerGeneratedAttribute",
            ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace.Name: "System" } },
        }) == true;
    }
}
