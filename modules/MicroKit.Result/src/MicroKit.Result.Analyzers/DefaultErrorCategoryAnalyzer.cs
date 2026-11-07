namespace MicroKit.Result.Analyzers;

/// <summary>
/// MKR002: a concrete subtype of <c>MicroKit.Result.Error</c> that keeps the default category, <c>Technical</c>.
/// </summary>
/// <remarks>
/// <c>Error.Category</c> defaults to <c>Technical</c>, which MicroKit.Result.AspNetCore answers with HTTP 500. A
/// domain error that forgets to override it reports a client error as a server fault. ADR-RESULT-001 D5 keeps the
/// default in code, so other modules' builds do not break, and makes the forgotten override a warning. A type is
/// not reported when it, or a type between it and <c>Error</c>, overrides <c>Category</c>; abstract types are not
/// reported, their concrete subtypes are.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultErrorCategoryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKR002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Error keeps the default Technical category",
        messageFormat: "'{0}' does not override Category: it is a Technical error, an internal fault (HTTP 500 in MicroKit.Result.AspNetCore)",
        category: DiagnosticCategories.Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Override Category with the kind of failure (NotFound, Validation, Conflict…), or build the error "
                   + "with a factory of Error (Error.NotFound(code, message)…). Override it with Technical explicitly "
                   + "when the error really is an internal fault.",
        helpLinkUri: "https://github.com/michaelatsey/microkit/blob/main/modules/MicroKit.Result/docs/guides/usage.md#mkr002-an-error-keeps-the-default-category");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var error = start.Compilation.GetTypeByMetadataName("MicroKit.Result.Error");
            if (error is null)
                return;

            start.RegisterSymbolAction(c => Analyze(c, error), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol error)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || SymbolEqualityComparer.Default.Equals(type, error))
            return;

        // Walk from the type up to Error: an override of Category anywhere on the way settles it.
        var derivesFromError = false;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, error))
            {
                derivesFromError = true;
                break;
            }

            foreach (var member in current.GetMembers("Category"))
            {
                if (member is IPropertySymbol { IsOverride: true })
                    return;
            }
        }

        if (!derivesFromError)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, type.Locations[0], type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
