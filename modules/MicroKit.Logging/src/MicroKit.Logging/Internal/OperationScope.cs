using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Returned by <see cref="ILogScopeFactory.BeginOperationScope()"/>.
/// On dispose: restores the previous <see cref="LogContextAccessor"/> state and disposes the MEL scope.
/// Dispose allocates zero bytes.
/// </summary>
internal sealed class OperationScope : IDisposable
{
    // Sentinel IDisposable for loggers where BeginScope returns null (e.g. NullLogger)
    private static readonly IDisposable s_nullScope = new NullScope();

    private readonly OperationContext? _previousContext;
    private readonly IDisposable _melScope;
    private readonly ILogger _logger;
    private bool _disposed;

    internal OperationScope(OperationContext? previousContext, IDisposable? melScope, ILogger logger)
    {
        _previousContext = previousContext;
        _melScope = melScope ?? s_nullScope;
        _logger = logger;
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
    }

    private sealed class NullScope : IDisposable
    {
        public void Dispose() { }
    }
}
