namespace MicroKit.Messaging.Execution;

/// <summary>
/// Pass-through <see cref="IExecutionScopeFactory"/> that wraps
/// <see cref="IServiceScopeFactory"/> with no tenant or correlation hydration.
/// </summary>
/// <remarks>
/// This is the v1 default implementation. Register it as a singleton via
/// <c>AddMicroKitMessaging()</c>. It is automatically superseded when a
/// tenant-aware implementation is registered by a later call to
/// <c>AddTenantAwareExecution()</c> (from <c>MicroKit.Multitenancy</c>).
/// <para>
/// The <see cref="IExecutionContext"/> provided to <see cref="CreateScopeAsync"/> is
/// injected into the created scope's <see cref="IServiceProvider"/> so that any scoped
/// service that depends on <see cref="IExecutionContext"/> (e.g.
/// <c>DomainEventsDispatcher</c> in cascade-dispatch paths) receives the message-row values
/// (TenantId, CorrelationId, CausationId) rather than the default fresh-Guid factory value.
/// </para>
/// <para>
/// <strong>Contract for custom implementations:</strong> all implementations of
/// <see cref="IExecutionScopeFactory"/> must bridge the <c>context</c> parameter into the
/// created scope so that cascade outbox writes preserve the end-to-end tracing chain.
/// Ignoring <c>context</c> breaks CorrelationId propagation on the notification-handler path.
/// </para>
/// </remarks>
internal sealed class PassThroughExecutionScopeFactory : IExecutionScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new <see cref="PassThroughExecutionScopeFactory"/>.
    /// </summary>
    public PassThroughExecutionScopeFactory(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    /// <inheritdoc />
    public ValueTask<IExecutionScope> CreateScopeAsync(
        IExecutionContext context, CancellationToken ct = default)
        => ValueTask.FromResult<IExecutionScope>(
            new PassThroughExecutionScope(_scopeFactory.CreateAsyncScope(), context));
}
