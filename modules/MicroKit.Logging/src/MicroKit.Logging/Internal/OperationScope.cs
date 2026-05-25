using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Returned by <see cref="ILogScopeFactory.BeginOperationScope()"/>.
/// On dispose: restores the previous <see cref="LogContextAccessor"/> state, disposes the MEL scope,
/// emits <c>MicroKit.Logging.Scope.Disposed</c>, and disposes the associated Activity span.
/// Dispose allocates zero bytes on the fast path (no listeners).
/// </summary>
internal sealed class OperationScope : IDisposable
{
    // Sentinel IDisposable for loggers where BeginScope returns null (e.g. NullLogger)
    private static readonly IDisposable s_nullScope = new NullScope();

    private readonly OperationContext? _previousContext;
    private readonly IDisposable _melScope;
    private readonly ILogger _logger;
    private readonly Activity? _activity;
    private readonly long _startTimestamp;
    private bool _disposed;

    internal OperationScope(
        OperationContext? previousContext,
        IDisposable? melScope,
        ILogger logger,
        Activity? activity,
        long startTimestamp)
    {
        _previousContext = previousContext;
        _melScope = melScope ?? s_nullScope;
        _logger = logger;
        _activity = activity;
        _startTimestamp = startTimestamp;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var current = LogContextAccessor.CurrentContext;
        LogContextAccessor.CurrentContext = _previousContext;
        _melScope.Dispose();

        if (current is not null)
            _logger.OperationScopeDisposed(current.CorrelationId);

        LoggingDiagnosticEmitter.EmitScopeDisposed(
            scopeName: current?.OperationId ?? current?.CorrelationId ?? string.Empty,
            operationId: current?.OperationId ?? string.Empty,
            durationMs: Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds);

        // Dispose Activity AFTER ScopeDisposed so OTEL span duration matches DurationMs
        _activity?.Dispose();
    }

    private sealed class NullScope : IDisposable
    {
        public void Dispose() { }
    }
}
