using MicroKit.Multitenancy.Analyzers.Helpers;

namespace MicroKit.Multitenancy.Analyzers;

/// <summary>
/// MKT001: A concrete type implementing <c>ITenantEntity</c> without a non-nullable
/// <c>TenantId</c> property.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantEntityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKT001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Entity implementing ITenantEntity without TenantId property",
        messageFormat: "'{0}' implements ITenantEntity but does not declare a non-nullable TenantId property",
        category: DiagnosticCategories.Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every type implementing ITenantEntity must declare a non-nullable TenantId property. "
                   + "A missing or nullable TenantId breaks tenant isolation and the EF Core query filter. "
                   + "Inherit TenantId from a base class or declare it directly with a non-nullable type.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

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

        // Skip abstract types — they may defer TenantId to concrete subclasses
        if (type.IsAbstract)
            return;

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        if (!MultitenancySymbolHelper.ImplementsITenantEntity(type, context.Compilation))
            return;

        if (!MultitenancySymbolHelper.HasNonNullableTenantIdProperty(type, context.Compilation))
        {
            var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
        }
    }
}
