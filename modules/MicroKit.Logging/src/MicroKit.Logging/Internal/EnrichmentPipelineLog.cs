using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.Internal;

internal static partial class EnrichmentPipelineLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "MicroKit enricher '{EnricherType}' threw an unhandled exception and was skipped")]
    internal static partial void EnricherFailed(
        this ILogger logger, string enricherType, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "MicroKit operation scope created — CorrelationId='{CorrelationId}', OperationId='{OperationId}'")]
    internal static partial void OperationScopeCreated(
        this ILogger logger, string correlationId, string? operationId);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "MicroKit operation scope disposed — CorrelationId='{CorrelationId}'")]
    internal static partial void OperationScopeDisposed(
        this ILogger logger, string correlationId);
}
