using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Centralises all <see cref="DiagnosticSource.Write"/> calls for MicroKit.Logging.
/// Payloads are <c>readonly struct</c> types — value types that box on Write, eliminating
/// the anonymous-class allocation that would occur with <c>new { ... }</c> payloads.
/// </summary>
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
    Justification = "Payload types are concrete readonly structs. Their properties are referenced at call sites and preserved by the linker — no trimming risk.")]
internal static class LoggingDiagnosticEmitter
{
    private static DiagnosticListener Listener => MicroKitDiagnosticSource.Listener;

    internal static void EmitEnrichmentExecuted(int enricherCount, string operationId, long startTimestamp)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.EnrichmentExecuted)) return;
        Listener.Write(MicroKitDiagnosticSource.EnrichmentExecuted, new EnrichmentExecutedPayload(
            enricherCount, operationId, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds));
    }

    internal static void EmitEnrichmentFaulted(string enricherType, Exception exception, string operationId)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.EnrichmentFaulted)) return;
        Listener.Write(MicroKitDiagnosticSource.EnrichmentFaulted, new EnrichmentFaultedPayload(
            enricherType, exception, operationId));
    }

    internal static void EmitScopeCreated(string scopeName, string operationId, string correlationId)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.ScopeCreated)) return;
        Listener.Write(MicroKitDiagnosticSource.ScopeCreated, new ScopeCreatedPayload(
            scopeName, operationId, correlationId));
    }

    internal static void EmitScopeDisposed(string scopeName, string operationId, long startTimestamp)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.ScopeDisposed)) return;
        Listener.Write(MicroKitDiagnosticSource.ScopeDisposed, new ScopeDisposedPayload(
            scopeName, operationId, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds));
    }

    internal static void EmitCorrelationGenerated(string correlationId)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.CorrelationGenerated)) return;
        Listener.Write(MicroKitDiagnosticSource.CorrelationGenerated, new CorrelationGeneratedPayload(correlationId));
    }

    internal static void EmitCorrelationResolved(string correlationId, string source)
    {
        if (!Listener.IsEnabled(MicroKitDiagnosticSource.CorrelationResolved)) return;
        Listener.Write(MicroKitDiagnosticSource.CorrelationResolved, new CorrelationResolvedPayload(correlationId, source));
    }
}
