namespace MicroKit.Logging.Analyzers;

/// <summary>MKL0041: Expensive expression passed to a log call without an <c>IsEnabled</c> guard.</summary>
/// <remarks>
/// When a log level is disabled, arguments to log calls are still evaluated. Method calls in log
/// arguments can cause unnecessary allocations and CPU work. Guard the call with
/// <c>if (logger.IsEnabled(LogLevel.X))</c> or use <c>[LoggerMessage]</c> source generation.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MKL0041Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKL0041";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Expensive expression in log argument without IsEnabled guard",
        messageFormat: "Log argument contains a method call that is evaluated even when the log level is disabled. Wrap the log call in 'if (logger.IsEnabled(LogLevel.{0}))' or use [LoggerMessage].",
        category: DiagnosticCategories.Performance,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Log arguments are eagerly evaluated by the runtime, even when the configured "
                   + "log level would discard the message. Method calls in log arguments can cause "
                   + "unnecessary allocations and CPU work at high call frequency. "
                   + "Use 'if (logger.IsEnabled(LogLevel.X))' to guard the block, "
                   + "or switch to [LoggerMessage] source generation for zero-allocation logging.",
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

        // Generic ILogger.Log(level, ...) — level is runtime, skip
        if (string.Equals(invocation.TargetMethod.Name, "Log", StringComparison.Ordinal))
            return;

        var logLevelName = LoggerCallHelper.GetLogLevelName(invocation.TargetMethod.Name);
        if (logLevelName is null)
            return;

        // Check if any non-message argument contains a method invocation (expensive)
        if (!HasExpensiveArgument(invocation))
            return;

        // If already inside an IsEnabled guard for the same logger, this is fine
        if (IsInsideIsEnabledGuard(invocation, logLevelName, context.Compilation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            logLevelName));
    }

    private static bool HasExpensiveArgument(IInvocationOperation invocation)
    {
        foreach (var arg in invocation.Arguments)
        {
            // Skip the message/format string argument
            var paramName = arg.Parameter?.Name;
            if (string.Equals(paramName, "message", StringComparison.Ordinal) ||
                string.Equals(paramName, "messageFormat", StringComparison.Ordinal) ||
                string.Equals(paramName, "logLevel", StringComparison.Ordinal) ||
                string.Equals(paramName, "eventId", StringComparison.Ordinal) ||
                string.Equals(paramName, "exception", StringComparison.Ordinal))
            {
                continue;
            }

            var value = UnwrapConversions(arg.Value);

            // Immediate method call is expensive
            if (value.Kind == OperationKind.Invocation)
                return true;

            // Object creation is also expensive
            if (value.Kind == OperationKind.ObjectCreation)
                return true;

            // params object[] args — the compiler wraps individual values in an implicit array creation.
            // Look inside the array elements for expensive expressions.
            if (value is IArrayCreationOperation arrayCreation &&
                arrayCreation.Initializer is not null)
            {
                foreach (var element in arrayCreation.Initializer.ElementValues)
                {
                    var elementValue = UnwrapConversions(element);
                    if (elementValue.Kind == OperationKind.Invocation ||
                        elementValue.Kind == OperationKind.ObjectCreation)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsInsideIsEnabledGuard(
        IInvocationOperation logInvocation,
        string logLevelName,
        Compilation compilation)
    {
        // Walk up the syntax tree looking for an enclosing if-statement whose condition
        // is a call to logger.IsEnabled(LogLevel.X) with a matching level.
        var logSyntax = logInvocation.Syntax;
        var node = logSyntax.Parent;

        while (node is not null)
        {
            if (node is IfStatementSyntax ifStatement)
            {
                if (ConditionIsIsEnabledForLevel(ifStatement.Condition, logLevelName, compilation))
                    return true;
            }

            node = node.Parent;
        }

        return false;
    }

    private static bool ConditionIsIsEnabledForLevel(
        ExpressionSyntax condition,
        string expectedLevelName,
        Compilation compilation)
    {
        if (condition is not InvocationExpressionSyntax call)
            return false;

        // Method must be named "IsEnabled"
        var memberAccess = call.Expression as MemberAccessExpressionSyntax;
        if (memberAccess is null) return false;

        if (!string.Equals(memberAccess.Name.Identifier.ValueText, "IsEnabled", StringComparison.Ordinal))
            return false;

        // Single argument must be a LogLevel member access matching expectedLevelName
        var args = call.ArgumentList.Arguments;
        if (args.Count != 1) return false;

        if (args[0].Expression is not MemberAccessExpressionSyntax levelAccess)
            return false;

        return string.Equals(
            levelAccess.Name.Identifier.ValueText,
            expectedLevelName,
            StringComparison.Ordinal);
    }

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion && conversion.IsImplicit)
            operation = conversion.Operand;
        return operation;
    }
}
