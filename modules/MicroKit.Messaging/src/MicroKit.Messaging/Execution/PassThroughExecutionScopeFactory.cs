namespace MicroKit.Messaging.Execution;

/// <summary>
/// Pass-through <see cref="IExecutionScopeFactory"/> that wraps
/// <see cref="IServiceScopeFactory"/> with no tenant or correlation hydration.
/// </summary>
/// <remarks>
/// <para>
/// The v1 default, registered as a singleton by <c>AddMicroKitMessaging()</c> with a plain
/// <c>AddSingleton</c>. A host that wants tenant-aware scopes registers its own
/// <see cref="IExecutionScopeFactory"/> afterwards; Microsoft DI resolves the last
/// registration, so the later one wins. No such implementation ships in MicroKit today.
/// </para>
/// <para>
/// <b>How the context reaches the scope.</b> The <see cref="IExecutionContext"/> passed to
/// <see cref="CreateScopeAsync"/> is written into the new scope's
/// <see cref="ExecutionContextHolder"/> before the scope is returned, and
/// <see cref="IExecutionContext"/> is registered to resolve through that holder. Constructor
/// injection therefore sees the message-row values — which is the whole point, and which a
/// service-provider wrapper alone could not achieve: Microsoft DI activates constructor
/// dependencies from its own scope and never consults a wrapper. That was L0 finding #21, and the
/// holder is its fix. <see cref="PassThroughExecutionScope"/> additionally keeps the wrapper, so a
/// direct <c>GetService(typeof(IExecutionContext))</c> call resolves to the same context.
/// </para>
/// <para>
/// <b>Contract for custom implementations:</b> bridge the <c>context</c> parameter into the
/// scope you return, in a way constructor injection can observe — a scoped holder, an ambient
/// value, or your own registration. Exposing it only through a wrapped
/// <see cref="IServiceProvider"/> is not enough, and loses the end-to-end tracing chain for
/// anything that reads the context inside a message scope.
/// </para>
/// <para>
/// A host that registers its own <see cref="IExecutionContext"/> — a tenant-aware one, say —
/// replaces the holder-backed registration and takes responsibility for hydration itself; that
/// host supplies its own <see cref="IExecutionScopeFactory"/> too.
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
    {
        var scope = _scopeFactory.CreateAsyncScope();

        // Written before the scope is handed out, so the first service activated from it already
        // sees the message-row context. Resolving the holder does not resolve IExecutionContext,
        // so this cannot capture a stale value.
        scope.ServiceProvider.GetRequiredService<ExecutionContextHolder>().Context = context;

        return ValueTask.FromResult<IExecutionScope>(new PassThroughExecutionScope(scope, context));
    }
}
