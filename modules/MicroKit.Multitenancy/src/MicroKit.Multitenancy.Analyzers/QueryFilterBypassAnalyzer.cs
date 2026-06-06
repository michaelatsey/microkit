using MicroKit.Multitenancy.Analyzers.Helpers;

namespace MicroKit.Multitenancy.Analyzers;

/// <summary>
/// MKT002: <c>IgnoreQueryFilters()</c> called without a <c>// [MTK-BYPASS]</c> justification comment.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryFilterBypassAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKT002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "IgnoreQueryFilters() called without justification comment",
        messageFormat: "IgnoreQueryFilters() bypasses tenant isolation — add '// [MTK-BYPASS] <reason>' on the same line or the immediately preceding non-blank line",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every call to IgnoreQueryFilters() on a multi-tenant query requires a justification comment "
                   + "matching '// [MTK-BYPASS]' to document intentional cross-tenant data access. "
                   + "Place the comment on the same line as the call or on the immediately preceding non-blank line.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!MultitenancySymbolHelper.IsIgnoreQueryFiltersMethod(
            invocation.TargetMethod, context.Compilation))
        {
            return;
        }

        if (MultitenancySymbolHelper.HasBypassComment(invocation))
            return;

        var location = GetMethodNameLocation(invocation);
        context.ReportDiagnostic(Diagnostic.Create(Rule, location));
    }

    private static Location GetMethodNameLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            return memberAccess.Name.GetLocation();

        return invocation.Syntax.GetLocation();
    }
}
