namespace MicroKit.Logging.AspNetCore;

/// <summary>
/// Options for the MicroKit ASP.NET Core logging integration.
/// Configure via <see cref="AspNetCoreLoggingServiceCollectionExtensions.AddMicroKitAspNetCoreLogging"/>.
/// </summary>
public sealed class AspNetCoreLoggingOptions
{
    /// <summary>
    /// The HTTP header name used to read the inbound correlation ID and write it to the response.
    /// Default: <c>X-Correlation-ID</c>.
    /// </summary>
    public string CorrelationIdHeader { get; set; } = "X-Correlation-ID";

    /// <summary>
    /// When <see langword="true"/> (default), the correlation ID is written to the response header
    /// specified by <see cref="CorrelationIdHeader"/> before the response body is sent.
    /// Set to <see langword="false"/> to suppress correlation ID propagation to callers.
    /// </summary>
    public bool PropagateCorrelationId { get; set; } = true;
}
