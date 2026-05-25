namespace MicroKit.Logging;

/// <summary>
/// Factory for creating structured log scopes that activate an <see cref="IOperationContext"/>
/// for the current async execution flow.
/// </summary>
/// <remarks>
/// <para>
/// Scope creation must allocate ≤ 128 bytes and complete in ≤ 200 ns.
/// Disposing the returned scope must allocate 0 bytes.
/// </para>
/// <para>
/// The scope is async-flow-aware: child tasks and continuations inherit the active context.
/// Disposing the scope restores the previous context.
/// </para>
/// </remarks>
public interface ILogScopeFactory
{
    /// <summary>
    /// Begins a new operation scope with an auto-generated <see cref="LogPropertyNames.CorrelationId"/>.
    /// </summary>
    /// <returns>A disposable scope. Disposing ends the scope and restores the prior context.</returns>
    IDisposable BeginOperationScope();

    /// <summary>
    /// Begins a new operation scope with the specified <see cref="LogPropertyNames.CorrelationId"/>.
    /// Use this overload when propagating a correlation ID from an inbound request header.
    /// </summary>
    /// <param name="correlationId">The correlation ID to use for this scope. Must not be null or empty.</param>
    /// <returns>A disposable scope. Disposing ends the scope and restores the prior context.</returns>
    IDisposable BeginOperationScope(string correlationId);

    /// <summary>
    /// Begins a new operation scope with full context initialization via <see cref="OperationScopeOptions"/>.
    /// Adding new context fields in the future is a non-breaking change via the options record.
    /// </summary>
    /// <param name="options">The scope options. <see cref="OperationScopeOptions.CorrelationId"/> must not be null or empty.</param>
    /// <returns>A disposable scope. Disposing ends the scope and restores the prior context.</returns>
    IDisposable BeginOperationScope(OperationScopeOptions options);
}
