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
/// <b>Known defect — the context bridge reaches less than it appears to.</b> The
/// <see cref="IExecutionContext"/> passed to <see cref="CreateScopeAsync"/> is exposed by
/// wrapping the scope's <see cref="IServiceProvider"/>, and that wrapper is consulted only for
/// a <b>direct</b> <c>GetService(typeof(IExecutionContext))</c> call on
/// <see cref="IExecutionScope.ServiceProvider"/>. It is <b>not</b> consulted when the container
/// activates a service that takes <see cref="IExecutionContext"/> as a <b>constructor
/// parameter</b> — those are resolved from the real scope and get the default scoped
/// registration instead, a fresh <c>CorrelationId</c> with a null <c>TenantId</c>. Constructor
/// injection of <see cref="IExecutionContext"/> inside a message scope therefore does not see
/// the message row's values. Tracked as L0 finding #21; the fix is a scoped context holder and
/// is a code change, deliberately not made here.
/// </para>
/// <para>
/// <b>Contract for custom implementations:</b> bridge the <c>context</c> parameter into the
/// scope you return. Ignoring it loses the end-to-end tracing chain for anything that reads
/// the context inside a message scope.
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
