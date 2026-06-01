using MicroKit.Persistence.Analyzers.Helpers;

namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// MKP001: <c>IReadRepository&lt;T&gt;</c> implementation calls <c>CommitAsync</c> or <c>SaveChangesAsync</c>.
/// MKP002: <c>IReadRepository&lt;T&gt;</c> implementation calls or declares <c>AddAsync</c>,
/// <c>UpdateAsync</c>, or <c>DeleteAsync</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReadRepositoryMutationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for commit/save violations in read repositories.</summary>
    public const string MKP001Id = "MKP001";

    /// <summary>Diagnostic ID for write-method violations in read repositories.</summary>
    public const string MKP002Id = "MKP002";

    private static readonly DiagnosticDescriptor MKP001Rule = new(
        id: MKP001Id,
        title: "Read repository calls CommitAsync or SaveChangesAsync",
        messageFormat: "'{0}' must not be called inside an IReadRepository implementation — read repositories are read-only",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IReadRepository<T> implementations must never commit or save changes. "
                   + "Use IRepository<T> and IUnitOfWork.CommitAsync() for write operations.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{MKP001Id}");

    private static readonly DiagnosticDescriptor MKP002Rule = new(
        id: MKP002Id,
        title: "Read repository declares or calls a write method",
        messageFormat: "'{0}' is a write operation and must not appear in an IReadRepository implementation",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IReadRepository<T> implementations must never expose or call AddAsync, UpdateAsync, or DeleteAsync. "
                   + "Use IRepository<T> for write operations.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{MKP002Id}");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MKP001Rule, MKP002Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Axis 1: invocations of forbidden methods inside a read repo body
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);

        // Axis 2: forbidden methods declared on a read repo implementation (architect Note 1)
        context.RegisterSymbolAction(AnalyzeMethodDeclaration, SymbolKind.Method);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var methodName = invocation.TargetMethod.Name;

        bool isCommitMethod = IsCommitMethod(methodName);
        bool isWriteMethod  = IsWriteMethod(methodName);

        if (!isCommitMethod && !isWriteMethod)
            return;

        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null)
            return;

        if (!PersistenceSymbolHelper.IsReadRepositoryImplementation(containingType, context.Compilation))
            return;

        var location = invocation.Syntax.GetLocation();

        if (isCommitMethod)
            context.ReportDiagnostic(Diagnostic.Create(MKP001Rule, location, methodName));
        else
            context.ReportDiagnostic(Diagnostic.Create(MKP002Rule, location, methodName));
    }

    private static void AnalyzeMethodDeclaration(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation))
            return;

        if (!IsWriteMethod(method.Name) && !IsCommitMethod(method.Name))
            return;

        if (!PersistenceSymbolHelper.IsReadRepositoryImplementation(method.ContainingType, context.Compilation))
            return;

        var location = method.Locations.Length > 0 ? method.Locations[0] : Location.None;
        context.ReportDiagnostic(Diagnostic.Create(MKP002Rule, location, method.Name));
    }

    private static bool IsCommitMethod(string name) =>
        string.Equals(name, "CommitAsync",      StringComparison.Ordinal) ||
        string.Equals(name, "SaveChangesAsync", StringComparison.Ordinal) ||
        string.Equals(name, "SaveChanges",      StringComparison.Ordinal);

    private static bool IsWriteMethod(string name) =>
        string.Equals(name, "AddAsync",    StringComparison.Ordinal) ||
        string.Equals(name, "UpdateAsync", StringComparison.Ordinal) ||
        string.Equals(name, "DeleteAsync", StringComparison.Ordinal);
}
