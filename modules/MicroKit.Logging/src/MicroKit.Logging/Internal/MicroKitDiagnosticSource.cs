using System.Diagnostics;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Singleton <see cref="DiagnosticListener"/> for all MicroKit.Logging instrumentation events.
/// Event payloads are anonymous objects — shape is documented at each emission site.
/// </summary>
/// <remarks>
/// Event name constants here are the internal mirror of
/// <c>MicroKit.Logging.Diagnostics.DiagnosticEventNames</c> (public).
/// The duplication is intentional: Core cannot depend on the Diagnostics package.
/// Values must remain byte-for-byte identical with those public constants.
/// </remarks>
internal static class MicroKitDiagnosticSource
{
    internal static readonly DiagnosticListener Listener =
        new("MicroKit.Logging");

    internal const string EnrichmentExecuted   = "MicroKit.Logging.Enrichment.Executed";
    internal const string EnrichmentFaulted    = "MicroKit.Logging.Enrichment.Faulted";
    internal const string ScopeCreated         = "MicroKit.Logging.Scope.Created";
    internal const string ScopeDisposed        = "MicroKit.Logging.Scope.Disposed";
    internal const string CorrelationResolved  = "MicroKit.Logging.Correlation.Resolved";
    internal const string CorrelationGenerated = "MicroKit.Logging.Correlation.Generated";
}
