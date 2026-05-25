using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Centralises all <see cref="DiagnosticSource.Write"/> calls for MicroKit.Logging.
/// Suppressing IL2026 here is safe: payloads are anonymous types whose properties are
/// compiler-generated and never subject to .NET trimming.
/// </summary>
internal static class LoggingDiagnosticEmitter
{
    private static DiagnosticListener Listener => MicroKitDiagnosticSource.Listener;

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Anonymous type properties used in diagnostic payloads are compiler-generated and not trimmed.")]
    internal static void EmitEnrichmentExecuted(int enricherCount, string operationId, double elapsedMs)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.EnrichmentExecuted)) return;
        // Shape: { int EnricherCount, string OperationId, double ElapsedMs }
        Listener.Write(MicroKitDiagnosticSource.EnrichmentExecuted, new
        {
            EnricherCount = enricherCount,
            OperationId = operationId,
            ElapsedMs = elapsedMs
        });
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Anonymous type properties used in diagnostic payloads are compiler-generated and not trimmed.")]
    internal static void EmitEnrichmentFaulted(string enricherType, Exception exception, string operationId)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.EnrichmentFaulted)) return;
        // Shape: { string EnricherType, Exception Exception, string OperationId }
        Listener.Write(MicroKitDiagnosticSource.EnrichmentFaulted, new
        {
            EnricherType = enricherType,
            Exception = exception,
            OperationId = operationId
        });
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Anonymous type properties used in diagnostic payloads are compiler-generated and not trimmed.")]
    internal static void EmitScopeCreated(string scopeName, string operationId, string correlationId)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.ScopeCreated)) return;
        // Shape: { string ScopeName, string OperationId, string CorrelationId }
        Listener.Write(MicroKitDiagnosticSource.ScopeCreated, new
        {
            ScopeName = scopeName,
            OperationId = operationId,
            CorrelationId = correlationId
        });
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Anonymous type properties used in diagnostic payloads are compiler-generated and not trimmed.")]
    internal static void EmitScopeDisposed(string scopeName, string operationId, double durationMs)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.ScopeDisposed)) return;
        // Shape: { string ScopeName, string OperationId, double DurationMs }
        Listener.Write(MicroKitDiagnosticSource.ScopeDisposed, new
        {
            ScopeName = scopeName,
            OperationId = operationId,
            DurationMs = durationMs
        });
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Anonymous type properties used in diagnostic payloads are compiler-generated and not trimmed.")]
    internal static void EmitCorrelationGenerated(string correlationId)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.CorrelationGenerated)) return;
        // Shape: { string CorrelationId }
        Listener.Write(MicroKitDiagnosticSource.CorrelationGenerated, new
        {
            CorrelationId = correlationId
        });
    }

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Anonymous type properties used in diagnostic payloads are compiler-generated and not trimmed.")]
    internal static void EmitCorrelationResolved(string correlationId, string source)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.CorrelationResolved)) return;
        // Shape: { string CorrelationId, string Source }
        Listener.Write(MicroKitDiagnosticSource.CorrelationResolved, new
        {
            CorrelationId = correlationId,
            Source = source
        });
    }
}
