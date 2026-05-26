namespace MicroKit.Logging.Analyzers;

/// <summary>MKL0031: Sensitive data identifier used as a structured log property name.</summary>
/// <remarks>
/// Logging sensitive values (passwords, tokens, secrets) creates a compliance and security risk.
/// This rule fires as an error — logging a property named "password" is always wrong.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MKL0031Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID emitted by this analyzer.</summary>
    public const string DiagnosticId = "MKL0031";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Sensitive data identifier used as log property name",
        messageFormat: "'{0}' is a sensitive identifier — logging this value may expose credentials or PII. Remove the property or use a redacted placeholder.",
        category: DiagnosticCategories.Security,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Using identifiers such as 'password', 'secret', or 'token' as structured log property "
                   + "names may leak sensitive data into log stores, monitoring systems, and crash reports. "
                   + "Remove the property from the log call or replace its value with a redacted placeholder.",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

    // Lowercase identifiers considered sensitive
    private static readonly string[] SensitiveTerms =
    [
        "password", "passwd", "pwd",
        "secret", "secrets",
        "token", "accesstoken", "refreshtoken", "bearertoken",
        "apikey", "api_key", "apitoken",
        "creditcard", "cardnumber", "cvv", "cvc",
        "ssn", "socialsecuritynumber",
        "pin",
        "privatekey", "privkey",
        "authorization", "auth",
    ];

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

        // Look for string literal values used as the message template
        // and check if any {placeholder} within them is a sensitive term
        if (messageArg.Value is ILiteralOperation literal &&
            literal.ConstantValue.HasValue &&
            literal.ConstantValue.Value is string template)
        {
            CheckTemplatePlaceholders(template, messageArg.Value.Syntax.GetLocation(), context);
        }
    }

    private static void CheckTemplatePlaceholders(
        string template,
        Location location,
        OperationAnalysisContext context)
    {
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] != '{')
            {
                i++;
                continue;
            }

            // Skip escaped {{
            if (i + 1 < template.Length && template[i + 1] == '{')
            {
                i += 2;
                continue;
            }

            var start = i + 1;
            var end = template.IndexOf('}', start);
            if (end < 0) break;

            // Extract placeholder name; strip format specifier (e.g. {Value:N2} → "Value")
            var placeholder = template.Substring(start, end - start);
            var colonIndex = placeholder.IndexOf(':');
            if (colonIndex >= 0)
                placeholder = placeholder.Substring(0, colonIndex);

            if (IsSensitiveTerm(placeholder))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, placeholder));
            }

            i = end + 1;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="name"/> matches a known sensitive identifier
    /// (case-insensitive, ignoring underscores and hyphens).
    /// </summary>
    public static bool IsSensitiveTerm(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        // Normalise: lowercase, strip separators
        var normalized = name.Replace("_", "").Replace("-", "").ToLowerInvariant();

        foreach (var term in SensitiveTerms)
        {
            if (string.Equals(normalized, term, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
