using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.AspNetCore;

internal static partial class CorrelationIdMiddlewareLog
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "MicroKit: CorrelationId '{CorrelationId}' received from '{Header}' request header")]
    internal static partial void CorrelationIdFromHeader(
        this ILogger logger, string correlationId, string header);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "MicroKit: CorrelationId '{CorrelationId}' generated — header '{Header}' was absent")]
    internal static partial void CorrelationIdGenerated(
        this ILogger logger, string correlationId, string header);
}
