namespace MicroKit.Messaging.Execution;

/// <summary>
/// Pass-through <see cref="IExecutionScope"/> that wraps an <see cref="AsyncServiceScope"/>
/// and injects the message-row <see cref="IExecutionContext"/> into the scope's service
/// provider. Used by <see cref="PassThroughExecutionScopeFactory"/> as the v1 default.
/// </summary>
internal sealed class PassThroughExecutionScope : IExecutionScope
{
    private readonly AsyncServiceScope _scope;
    private readonly IServiceProvider _serviceProvider;

    internal PassThroughExecutionScope(AsyncServiceScope scope, IExecutionContext context)
    {
        _scope = scope;
        // Wrap the scope's provider so that IExecutionContext resolves to the message-row
        // context (TenantId, CorrelationId, CausationId) rather than the default scoped
        // factory that produces a fresh Guid. This ensures cascade outbox writes produced
        // by DomainEventsDispatcher preserve the end-to-end tracing chain.
        _serviceProvider = new ContextAwareServiceProvider(scope.ServiceProvider, context);
    }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider => _serviceProvider;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    /// <summary>
    /// Thin <see cref="IServiceProvider"/> wrapper that overrides <see cref="IExecutionContext"/>
    /// resolution with a pre-built instance, delegating all other lookups to the inner provider.
    /// </summary>
    private sealed class ContextAwareServiceProvider(
        IServiceProvider inner,
        IExecutionContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IExecutionContext) ? context : inner.GetService(serviceType);
    }
}
