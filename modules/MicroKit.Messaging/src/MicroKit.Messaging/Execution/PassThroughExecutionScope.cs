namespace MicroKit.Messaging.Execution;

/// <summary>
/// Pass-through <see cref="IExecutionScope"/> that wraps an <see cref="AsyncServiceScope"/>
/// without any additional context hydration. Used by <see cref="PassThroughExecutionScopeFactory"/>
/// as the v1 default when no tenant-aware scope factory is registered.
/// </summary>
internal sealed class PassThroughExecutionScope : IExecutionScope
{
    private readonly AsyncServiceScope _scope;

    internal PassThroughExecutionScope(AsyncServiceScope scope)
        => _scope = scope;

    /// <inheritdoc />
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
        => _scope.DisposeAsync();
}
