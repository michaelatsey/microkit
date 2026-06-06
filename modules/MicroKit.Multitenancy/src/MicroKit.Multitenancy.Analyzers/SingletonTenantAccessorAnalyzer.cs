using MicroKit.Multitenancy.Analyzers.Helpers;

namespace MicroKit.Multitenancy.Analyzers;

/// <summary>
/// MKT003: <c>ITenantContextAccessor</c> injected into a service registered as Singleton.
/// Detects <c>AddSingleton&lt;TService, TImpl&gt;()</c> and <c>AddSingleton&lt;T&gt;()</c>
/// where the implementation type injects <c>ITenantContextAccessor</c> in a constructor.
/// The <c>typeof(T)</c> form is a known v1 limitation — not detected.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonTenantAccessorAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKT003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "ITenantContextAccessor injected in a singleton service",
        messageFormat: "'{0}' is registered as a Singleton but injects ITenantContextAccessor — register as Scoped or Transient instead",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ITenantContextAccessor is backed by AsyncLocal and must never be injected into a Singleton service. "
                   + "Singleton services share state across DI scopes, causing tenant context to leak between requests. "
                   + "Register the consuming service as Scoped (preferred) or Transient instead.",
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
        var method     = invocation.TargetMethod;

        if (method.Name != "AddSingleton")
            return;

        // typeof(T) form uses a non-generic overload — not detected in v1
        if (!method.IsGenericMethod || method.TypeArguments.IsEmpty)
            return;

        // Architect issue #1: verify the receiver is IServiceCollection to prevent false positives.
        // For extension method calls in extension syntax, Instance is the receiver expression.
        // For static call syntax (uncommon), fall back to inspecting the first method parameter.
        var receiverType = invocation.Instance?.Type;
        if (receiverType is null && method.IsExtensionMethod && method.Parameters.Length > 0)
            receiverType = method.Parameters[0].Type;

        if (!MultitenancySymbolHelper.IsIServiceCollectionReceiver(receiverType, context.Compilation))
            return;

        // 2-arg form: AddSingleton<TService, TImpl>() → implementation is TypeArguments[1]
        // 1-arg form: AddSingleton<T>()              → implementation is TypeArguments[0]
        var implType = (method.TypeArguments.Length >= 2
            ? method.TypeArguments[1]
            : method.TypeArguments[0]) as INamedTypeSymbol;

        if (implType is null)
            return;

        if (!MultitenancySymbolHelper.HasITenantContextAccessorInConstructor(implType, context.Compilation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), implType.Name));
    }
}
