namespace MicroKit.Logging;

/// <summary>
/// Well-known execution order values for <see cref="ILogEnricher"/> and <see cref="IAsyncLogEnricher"/> implementations.
/// Lower values run first. Enrichers registered at the same order value have undefined relative ordering.
/// </summary>
public static class LogEnricherOrder
{
    /// <summary>
    /// Correlation enrichers run first (e.g., <see cref="LogPropertyNames.CorrelationId"/>,
    /// <see cref="LogPropertyNames.TraceId"/>, <see cref="LogPropertyNames.SpanId"/>).
    /// </summary>
    public const int Correlation = 100;

    /// <summary>
    /// Identity enrichers run after correlation (e.g., <see cref="LogPropertyNames.TenantId"/>,
    /// <see cref="LogPropertyNames.UserId"/>).
    /// </summary>
    public const int Identity = 200;

    /// <summary>
    /// Application-level enrichers (e.g., <see cref="LogPropertyNames.CommandName"/>,
    /// <see cref="LogPropertyNames.MessageId"/>, custom business properties).
    /// </summary>
    public const int Application = 300;

    /// <summary>Custom or user-defined enrichers that run after all built-in enrichers.</summary>
    public const int Custom = 1000;
}
