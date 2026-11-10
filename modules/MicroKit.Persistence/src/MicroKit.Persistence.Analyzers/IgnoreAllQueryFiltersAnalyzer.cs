using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP009: the parameterless <c>IgnoreQueryFilters()</c> disables every query filter of the query, tenant filters
/// included. Name the filters to ignore (<c>IgnoreQueryFilters(["name"])</c>), or use <c>WithDeleted()</c> for
/// soft-deleted aggregates (ADR-014).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreAllQueryFiltersAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP009";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "IgnoreQueryFilters() disables every query filter",
        messageFormat: "IgnoreQueryFilters() disables every query filter, tenant filters included; name the filters to ignore (IgnoreQueryFilters([\"name\"])), or use WithDeleted() for soft-deleted aggregates",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The parameterless IgnoreQueryFilters() turns off every query filter of the query, a tenant filter the "
                   + "code does not own included. EF Core 10 names query filters: ignore only the ones meant, with "
                   + "IgnoreQueryFilters([\"name\"]); MicroKit's soft delete has WithDeleted(). The parameterless overload is "
                   + "the only way to ignore an anonymous filter: suppress the warning there.",
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
            if (symbols.EfQueryableExtensions is not { } extensions)
                return; // EF Core is not referenced

            start.RegisterOperationAction(operation =>
            {
                var invocation = (IInvocationOperation)operation.Operation;
                if (IgnoresEveryFilter(invocation.TargetMethod, extensions))
                    operation.ReportDiagnostic(Diagnostic.Create(Rule, QueryChain.NameLocation(invocation)));
            }, OperationKind.Invocation);

            start.RegisterOperationAction(operation =>
            {
                var reference = (IMethodReferenceOperation)operation.Operation;
                if (IgnoresEveryFilter(reference.Method, extensions))
                    operation.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation()));
            }, OperationKind.MethodReference);
        });
    }

    // EntityFrameworkQueryableExtensions.IgnoreQueryFilters<T>(IQueryable<T>): the source is its only parameter.
    // The named overload takes the filter keys as a second one.
    private static bool IgnoresEveryFilter(IMethodSymbol method, INamedTypeSymbol extensions)
    {
        var definition = method.ReducedFrom ?? method;
        return QuerySymbols.Is(definition, extensions, "IgnoreQueryFilters") && definition.Parameters.Length == 1;
    }
}
