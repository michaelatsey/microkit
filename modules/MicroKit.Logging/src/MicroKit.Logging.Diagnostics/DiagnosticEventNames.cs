namespace MicroKit.Logging.Diagnostics;

/// <summary>
/// Canonical event names for the <c>MicroKit.Logging</c> <see cref="System.Diagnostics.DiagnosticListener"/>.
/// Subscribe via <see cref="System.Diagnostics.DiagnosticListener.AllListeners"/>, filter by listener
/// name <c>"MicroKit.Logging"</c>.
/// </summary>
/// <remarks>
/// These values are the public mirror of the internal constants in <c>MicroKit.Logging.Internal.MicroKitDiagnosticSource</c>.
/// The duplication is intentional: Core cannot depend on this package.
/// </remarks>
public static class DiagnosticEventNames
{
    /// <summary>
    /// Emitted after the synchronous enrichment pipeline completes.
    /// Payload: <c>{ int EnricherCount, string OperationId, double ElapsedMs }</c>.
    /// </summary>
    public const string EnrichmentExecuted = "MicroKit.Logging.Enrichment.Executed";

    /// <summary>
    /// Emitted when an enricher throws an unhandled exception and is skipped.
    /// Payload: <c>{ string EnricherType, Exception Exception, string OperationId }</c>.
    /// </summary>
    public const string EnrichmentFaulted = "MicroKit.Logging.Enrichment.Faulted";

    /// <summary>
    /// Emitted when a new operation scope is opened.
    /// Payload: <c>{ string ScopeName, string OperationId, string CorrelationId }</c>.
    /// </summary>
    public const string ScopeCreated = "MicroKit.Logging.Scope.Created";

    /// <summary>
    /// Emitted when an operation scope is disposed.
    /// Payload: <c>{ string ScopeName, string OperationId, double DurationMs }</c>.
    /// </summary>
    public const string ScopeDisposed = "MicroKit.Logging.Scope.Disposed";

    /// <summary>
    /// Emitted when a correlation ID is extracted from inbound context (e.g., an HTTP header).
    /// Payload: <c>{ string CorrelationId, string Source }</c>.
    /// </summary>
    public const string CorrelationResolved = "MicroKit.Logging.Correlation.Resolved";

    /// <summary>
    /// Emitted when a new correlation ID is auto-generated because none was provided.
    /// Payload: <c>{ string CorrelationId }</c>.
    /// </summary>
    public const string CorrelationGenerated = "MicroKit.Logging.Correlation.Generated";
}
