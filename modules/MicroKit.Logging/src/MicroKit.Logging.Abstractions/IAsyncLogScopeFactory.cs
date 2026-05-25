namespace MicroKit.Logging;

/// <summary>
/// Async counterpart to <see cref="ILogScopeFactory"/> for use when
/// <see cref="IAsyncLogEnricher"/> instances must be awaited before the scope is activated.
/// Resolve from DI alongside <see cref="ILogScopeFactory"/> — the same implementation satisfies both.
/// </summary>
public interface IAsyncLogScopeFactory
{
    /// <summary>
    /// Asynchronously begins a new operation scope with an auto-generated correlation ID,
    /// running all registered async enrichers before activating the scope.
    /// </summary>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>A disposable scope. Disposing ends the scope and restores the prior context.</returns>
    ValueTask<IDisposable> BeginOperationScopeAsync(CancellationToken ct = default);

    /// <summary>
    /// Asynchronously begins a new operation scope with the specified correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation ID. Must not be null or empty.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>A disposable scope. Disposing ends the scope and restores the prior context.</returns>
    ValueTask<IDisposable> BeginOperationScopeAsync(string correlationId, CancellationToken ct = default);

    /// <summary>
    /// Asynchronously begins a new operation scope with full context initialization.
    /// </summary>
    /// <param name="options">The scope options. <see cref="OperationScopeOptions.CorrelationId"/> must not be null or empty.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>A disposable scope. Disposing ends the scope and restores the prior context.</returns>
    ValueTask<IDisposable> BeginOperationScopeAsync(OperationScopeOptions options, CancellationToken ct = default);
}
