namespace MicroKit.Logging.Analyzers;

/// <summary>MKL0011: Interpolated string used as an ILogger message argument.</summary>
/// <remarks>
/// Interpolated strings prevent structured log sinks from indexing individual values.
/// Replace with a template string and positional arguments.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MKL0011Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKL0011";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Interpolated string used in log message",
        messageFormat: "Interpolated string in log call prevents structured indexing — use a template string with named placeholders instead",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "String interpolation ($\"...\") collapses structured values into a flat string. "
                   + "Structured log sinks cannot index individual properties. "
                   + "Use a message template with {PlaceholderName} syntax and pass values as arguments.",
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

        if (!LoggerCallHelper.IsLoggerCall(invocation, context.Compilation))
            return;

        var messageArg = LoggerCallHelper.FindMessageArgument(invocation);
        if (messageArg is null)
            return;

        var value = UnwrapConversions(messageArg.Value);

        if (value.Kind == OperationKind.InterpolatedString)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation()));
        }
    }

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion && conversion.IsImplicit)
            operation = conversion.Operand;
        return operation;
    }
}
