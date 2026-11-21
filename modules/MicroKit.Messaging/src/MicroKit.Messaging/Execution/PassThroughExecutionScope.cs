namespace MicroKit.Messaging.Execution;

/// <summary>
/// Pass-through <see cref="IExecutionScope"/> that wraps an <see cref="AsyncServiceScope"/> and
/// overrides <see cref="IExecutionContext"/> resolution with the message-row context. Used by
/// <see cref="PassThroughExecutionScopeFactory"/> as the v1 default.
/// </summary>
/// <remarks>
/// The factory has already written the context into the scope's
/// <see cref="ExecutionContextHolder"/>, which is what constructor injection reads. The wrapper
/// below covers the other direction — a direct <c>GetService</c> call on
/// <see cref="ServiceProvider"/> — so both resolve to the same instance.
/// </remarks>
internal sealed class PassThroughExecutionScope : IExecutionScope
{
    private readonly AsyncServiceScope _scope;
    private readonly IServiceProvider _serviceProvider;

    internal PassThroughExecutionScope(AsyncServiceScope scope, IExecutionContext context)
    {
        _scope = scope;
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
    /// <remarks>
    /// Serves direct <c>GetService</c> calls made against this instance. Constructor injection is
    /// covered by <see cref="ExecutionContextHolder"/> instead — the container activates
    /// constructor dependencies from the inner scope, which never sees this wrapper.
    /// </remarks>
    private sealed class ContextAwareServiceProvider(
        IServiceProvider inner,
        IExecutionContext context) : IServiceProvider, IKeyedServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IExecutionContext) ? context : inner.GetService(serviceType);

        // Keyed resolution reaches the scope unchanged (#188): a store's services are resolved
        // from the message scope by the store's key, and a wrapper that only implemented
        // IServiceProvider made every keyed lookup throw "doesn't support keyed services".
        public object? GetKeyedService(Type serviceType, object? serviceKey)
            => Keyed.GetKeyedService(serviceType, serviceKey);

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
            => Keyed.GetRequiredKeyedService(serviceType, serviceKey);

        private IKeyedServiceProvider Keyed => inner as IKeyedServiceProvider
            ?? throw new InvalidOperationException(
                "The scope's service provider does not support keyed services, which the message " +
                "stores are resolved through.");
    }
}
