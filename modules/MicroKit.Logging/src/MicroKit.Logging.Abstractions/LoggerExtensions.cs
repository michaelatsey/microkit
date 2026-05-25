using Microsoft.Extensions.Logging;

namespace MicroKit.Logging;

/// <summary>
/// Convenience extension methods on <see cref="ILogger"/> for creating MicroKit correlation scopes.
/// These are purely additive — they delegate to <see cref="ILogger.BeginScope{TState}(TState)"/>
/// with a structured dictionary matching canonical <see cref="LogPropertyNames"/> keys.
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// Begins a structured log scope with a <see cref="LogPropertyNames.CorrelationId"/> property.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="correlationId">The correlation ID to include in all log entries within this scope.</param>
    /// <returns>
    /// A disposable scope, or <see langword="null"/> if the logger does not support scopes.
    /// </returns>
    public static IDisposable? BeginCorrelationScope(this ILogger logger, string correlationId)
        => logger.BeginScope(new KeyValuePair<string, object?>[]
        {
            new(LogPropertyNames.CorrelationId, correlationId)
        });

    /// <summary>
    /// Begins a structured log scope with <see cref="LogPropertyNames.CorrelationId"/>
    /// and <see cref="LogPropertyNames.OperationId"/> properties.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="correlationId">The correlation ID.</param>
    /// <param name="operationId">The logical operation ID (e.g., a command name).</param>
    /// <returns>
    /// A disposable scope, or <see langword="null"/> if the logger does not support scopes.
    /// </returns>
    public static IDisposable? BeginCorrelationScope(this ILogger logger, string correlationId, string operationId)
        => logger.BeginScope(new KeyValuePair<string, object?>[]
        {
            new(LogPropertyNames.CorrelationId, correlationId),
            new(LogPropertyNames.OperationId, operationId)
        });
}
