namespace MicroKit.Logging.Analyzers;

/// <summary>
/// Shared helpers for detecting <c>ILogger</c> log-method invocations across multiple analyzers.
/// </summary>
internal static class LoggerCallHelper
{
    private static readonly string[] LogMethodNames =
    [
        "Log", "LogDebug", "LogTrace", "LogInformation",
        "LogWarning", "LogError", "LogCritical",
    ];

    private static readonly string[] LogLevelByMethodName =
    [
        "None",       // "Log" — level is determined by parameter
        "Debug",      // "LogDebug"
        "Trace",      // "LogTrace"
        "Information",// "LogInformation"
        "Warning",    // "LogWarning"
        "Error",      // "LogError"
        "Critical",   // "LogCritical"
    ];

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="invocation"/> targets one of the standard
    /// <c>ILogger</c> log extension methods or the <c>ILogger.Log</c> interface method.
    /// </summary>
    internal static bool IsLoggerCall(IInvocationOperation invocation, Compilation compilation)
    {
        var method = invocation.TargetMethod;

        if (!IsLogMethodName(method.Name))
            return false;

        var iloggerType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        if (iloggerType is null)
            return false;

        var containingType = method.ContainingType;

        // Direct ILogger.Log method (the interface method itself)
        if (SymbolEqualityComparer.Default.Equals(containingType, iloggerType))
            return true;

        // Extension methods defined on LoggerExtensions
        var extensionsType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.LoggerExtensions");
        if (extensionsType is not null &&
            SymbolEqualityComparer.Default.Equals(containingType, extensionsType))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the argument that carries the message/format string within a logger invocation.
    /// Returns <see langword="null"/> if no message argument can be determined.
    /// </summary>
    internal static IArgumentOperation? FindMessageArgument(IInvocationOperation invocation)
    {
        foreach (var arg in invocation.Arguments)
        {
            if (string.Equals(arg.Parameter?.Name, "message", StringComparison.Ordinal) ||
                string.Equals(arg.Parameter?.Name, "messageFormat", StringComparison.Ordinal))
            {
                return arg;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the <c>LogLevel</c> enum member name implied by a log method name.
    /// Returns <see langword="null"/> for the generic <c>Log</c> overload where the level is a parameter.
    /// </summary>
    internal static string? GetLogLevelName(string methodName) => methodName switch
    {
        "LogDebug"       => "Debug",
        "LogTrace"       => "Trace",
        "LogInformation" => "Information",
        "LogWarning"     => "Warning",
        "LogError"       => "Error",
        "LogCritical"    => "Critical",
        _                => null,
    };

    private static bool IsLogMethodName(string name)
    {
        foreach (var n in LogMethodNames)
        {
            if (string.Equals(n, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
