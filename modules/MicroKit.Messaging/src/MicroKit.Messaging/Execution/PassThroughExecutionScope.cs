namespace MicroKit.Messaging.Execution;

/// <summary>
/// Pass-through <see cref="IExecutionScope"/> that wraps an <see cref="AsyncServiceScope"/> and
/// overrides <see cref="IExecutionContext"/> resolution with the message-row context. Used by
/// <see cref="PassThroughExecutionScopeFactory"/> as the v1 default.
/// </summary>
/// <remarks>
/// The override applies to direct <c>GetService</c> calls on <see cref="ServiceProvider"/> only,
/// not to constructor injection — see the known defect on
/// <see cref="PassThroughExecutionScopeFactory"/>.
/// </remarks>
internal sealed class PassThroughExecutionScope : IExecutionScope
{
    private readonly AsyncServiceScope _scope;
    private readonly IServiceProvider _serviceProvider;

    internal PassThroughExecutionScope(AsyncServiceScope scope, IExecutionContext context)
    {
        _scope = scope;
        // Wrap the scope's provider so that a direct GetService<IExecutionContext>() resolves
        // to the message-row context (TenantId, CorrelationId, CausationId) rather than the
        // default scoped factory that produces a fresh Guid.
        //
        // This does NOT reach constructor-injected dependencies: MS DI activates them from its
        // own scope, which never sees this wrapper. L0 finding #21.
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
    /// Only <see cref="GetService"/> calls made against this instance are intercepted. The
    /// container activates constructor dependencies from the inner scope, so a service whose
    /// constructor takes <see cref="IExecutionContext"/> gets the default scoped registration,
    /// not <c>context</c>.
    /// </remarks>
    private sealed class ContextAwareServiceProvider(
        IServiceProvider inner,
        IExecutionContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IExecutionContext) ? context : inner.GetService(serviceType);
    }
}
