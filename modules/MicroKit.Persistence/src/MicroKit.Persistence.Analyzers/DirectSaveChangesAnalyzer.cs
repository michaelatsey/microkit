using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP003: <c>SaveChangesAsync</c> or <c>SaveChanges</c> called directly on a <c>DbContext</c>
/// outside of an <c>IUnitOfWork</c> implementation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectSaveChangesAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKP003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "SaveChangesAsync called directly — bypasses IUnitOfWork",
        messageFormat: "Direct call to '{0}' bypasses IUnitOfWork — call IUnitOfWork.CommitAsync() instead",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling SaveChangesAsync or SaveChanges directly on a DbContext bypasses the IUnitOfWork boundary "
                   + "and breaks the single-commit-per-command guarantee. "
                   + "Call IUnitOfWork.CommitAsync() instead. "
                   + "Classes implementing IUnitOfWork (e.g. EfUnitOfWork) are excluded from this rule. "
                   + "Custom DbContext subclasses overriding SaveChangesAsync for auditing should suppress "
                   + "with #pragma warning disable MKP003.",
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
        var methodName = invocation.TargetMethod.Name;

        if (methodName is not ("SaveChangesAsync" or "SaveChanges"))
            return;

        // Must be called on a DbContext (or derived) instance
        var receiverType = invocation.Instance?.Type;
        if (receiverType is null)
            return;

        if (!PersistenceSymbolHelper.IsOrDerivesFromDbContext(receiverType, context.Compilation))
            return;

        // Skip classes implementing IUnitOfWork — this is the legitimate save boundary
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is not null &&
            PersistenceSymbolHelper.IsUnitOfWorkImplementation(containingType, context.Compilation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), methodName));
    }
}
