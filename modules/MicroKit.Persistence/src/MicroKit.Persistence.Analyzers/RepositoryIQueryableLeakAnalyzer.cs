using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP005: A method on an <c>IRepository&lt;T&gt;</c> or <c>IReadRepository&lt;T&gt;</c>
/// implementation declares <c>IQueryable&lt;T&gt;</c> as its return type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepositoryIQueryableLeakAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP005";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Repository method exposes IQueryable<T> as return type",
        messageFormat: "'{0}' returns IQueryable<T>, which leaks EF Core concerns into consuming code — return IReadOnlyList<T> or IPagedResult<T> instead",
        category: DiagnosticCategories.Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Returning IQueryable<T> from a repository method leaks EF Core query composition outside the infrastructure layer. "
                   + "Callers can append arbitrary Where/Include clauses, bypassing specification enforcement and query auditing. "
                   + "Note: IAsyncEnumerable<T> is allowed — the concern is the IQueryable<T> signature specifically. "
                   + "Return IReadOnlyList<T>, IPagedResult<T>, or a concrete projection DTO instead.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            return;

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation))
            return;

        if (!PersistenceSymbolHelper.IsRepositoryImplementation(method.ContainingType, context.Compilation))
            return;

        // Unwrap Task<>/ValueTask<> wrapper before checking for IQueryable<T>
        var returnType = PersistenceSymbolHelper.UnwrapTaskType(method.ReturnType, context.Compilation);

        if (!PersistenceSymbolHelper.IsIQueryable(returnType, context.Compilation))
            return;

        var location = method.Locations.Length > 0 ? method.Locations[0] : Location.None;
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, method.Name));
    }
}
