namespace MicroKit.Logging.Analyzers;

/// <summary>MKL0012: String concatenation used as an ILogger message argument.</summary>
/// <remarks>
/// Concatenated strings prevent structured log sinks from indexing individual values.
/// Replace with a template string and positional arguments.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MKL0012Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKL0012";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "String concatenation used in log message",
        messageFormat: "String concatenation in log call prevents structured indexing — use a template string with named placeholders instead",
        category: DiagnosticCategories.Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "String concatenation (\"literal\" + value) collapses structured values into a flat string. "
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

        if (IsStringConcatenation(value))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation()));
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="operation"/> is a binary string
    /// concatenation (operator <c>+</c> where at least one operand is a string type).
    /// </summary>
    internal static bool IsStringConcatenation(IOperation operation)
    {
        if (operation is not IBinaryOperation binary)
            return false;

        if (binary.OperatorKind != BinaryOperatorKind.Add)
            return false;

        // At least one side must be a string type; the other may be non-string (converted via ToString)
        return IsStringType(binary.LeftOperand.Type) || IsStringType(binary.RightOperand.Type);
    }

    private static bool IsStringType(ITypeSymbol? type) =>
        type?.SpecialType == SpecialType.System_String;

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion && conversion.IsImplicit)
            operation = conversion.Operand;
        return operation;
    }
}
