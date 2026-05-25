using System.Diagnostics;

namespace MicroKit.Logging;

/// <summary>
/// Options controlling the MicroKit enrichment pipeline and operation context behavior.
/// Configure via <see cref="LoggingBuilderExtensions.AddMicroKitLogging"/>.
/// </summary>
public sealed class MicroKitLoggingOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), reads <see cref="Activity.Current"/> at scope creation
    /// to populate <see cref="IOperationContext.TraceId"/> and <see cref="IOperationContext.SpanId"/>.
    /// Set to <see langword="false"/> to disable Activity integration.
    /// </summary>
    public bool EnableActivityContextReading { get; set; } = true;

    /// <summary>
    /// Optional factory for generating correlation IDs. Must be thread-safe and never return
    /// <see langword="null"/> or an empty string. When <see langword="null"/> (default),
    /// a compact 32-character lowercase hex GUID is used.
    /// </summary>
    public Func<string>? CorrelationIdFactory { get; set; }
}
