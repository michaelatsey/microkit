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
            new PassThroughExecutionScope(_scopeFactory.CreateAsyncScope()));
}
