using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP004: <c>DbContext</c> (or a derived type) injected as a constructor parameter
/// in a class whose namespace is not considered infrastructure.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextInjectionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP004";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "DbContext injected outside infrastructure layer",
        messageFormat: "'{0}' derives from DbContext and is injected outside the infrastructure layer — inject IRepository<T> or IReadRepository<T> instead",
        category: DiagnosticCategories.Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Injecting DbContext directly outside the infrastructure layer couples application code to EF Core. "
                   + "Inject a typed repository (IRepository<T> or IReadRepository<T>) instead. "
                   + "Infrastructure layer is identified by namespace keywords: Infrastructure, Persistence, "
                   + "EntityFrameworkCore, Repository, Data. "
                   + "This is a best-effort heuristic — suppress with #pragma warning disable MKP004 "
                   + "for legitimate infrastructure classes that do not use conventional namespace naming.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

    // Namespace keywords that identify the infrastructure/persistence layer
    private static readonly string[] InfrastructureKeywords =
        ["Infrastructure", "Persistence", "EntityFrameworkCore", "Repository", "Data"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is not TypeKind.Class)
            return;

        var namespaceName = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (IsInfrastructureNamespace(namespaceName))
            return;

        foreach (var constructor in type.Constructors)
        {
            if (constructor.IsImplicitlyDeclared)
                continue;

            foreach (var parameter in constructor.Parameters)
            {
                if (!PersistenceSymbolHelper.IsOrDerivesFromDbContext(parameter.Type, context.Compilation))
                    continue;

                // Report at the parameter type location for the most useful IDE squiggle
                var location = GetParameterTypeLocation(parameter);
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, parameter.Type.Name));
            }
        }
    }

    private static Location GetParameterTypeLocation(IParameterSymbol parameter)
    {
        // Prefer the type node location over the symbol location for a more useful squiggle
        var syntaxRef = parameter.DeclaringSyntaxReferences.Length > 0
            ? parameter.DeclaringSyntaxReferences[0]
            : null;

        if (syntaxRef?.GetSyntax() is ParameterSyntax paramSyntax && paramSyntax.Type is not null)
            return paramSyntax.Type.GetLocation();

        return parameter.Locations.Length > 0 ? parameter.Locations[0] : Location.None;
    }

    private static bool IsInfrastructureNamespace(string namespaceName)
    {
        foreach (var keyword in InfrastructureKeywords)
        {
            if (namespaceName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
