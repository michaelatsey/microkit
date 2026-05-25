namespace MicroKit.Logging.Internal;

internal static class CorrelationIdGenerator
{
    /// <summary>
    /// Generates a unique 32-character lowercase hex correlation ID.
    /// One string allocation per call — the result string itself.
    /// </summary>
    internal static string Generate()
        => string.Create(32, Guid.NewGuid(), static (span, guid) => guid.TryFormat(span, out _, "N"));
}
