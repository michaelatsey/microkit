namespace MicroKit.Messaging.Execution;

/// <summary>
/// Scoped mutable slot holding the <see cref="IExecutionContext"/> for one execution scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because a service provider wrapper cannot reach constructor injection.</b>
/// <see cref="PassThroughExecutionScope"/> wraps the scope's <see cref="IServiceProvider"/> and
/// intercepts a direct <c>GetService(typeof(IExecutionContext))</c> call, but Microsoft DI
/// activates constructor dependencies from its own scope, which never sees that wrapper. Every
/// service taking <see cref="IExecutionContext"/> as a constructor parameter therefore received
/// the default scoped registration — a fresh <c>CorrelationId</c> with a null <c>TenantId</c> —
/// rather than the message-row context. The tenant was lost and the correlation chain was severed
/// at exactly the hop this module exists to preserve (L0 finding #21).
/// </para>
/// <para>
/// The indirection closes it: <see cref="IExecutionContext"/> resolves <i>through</i> this holder,
/// so populating the holder on a freshly created scope changes what constructor injection sees.
/// One scoped instance per scope, written once by
/// <see cref="PassThroughExecutionScopeFactory"/> before the scope is handed out and read-only in
/// practice afterwards.
/// </para>
/// <para>
/// The default value is a context carrying a fresh <c>CorrelationId</c> and nothing else, which is
/// what a scope created outside the messaging pipeline (an HTTP request, a test) gets.
/// </para>
/// </remarks>
internal sealed class ExecutionContextHolder
{
    /// <summary>Gets or sets the context for this scope.</summary>
    public IExecutionContext Context { get; set; } =
        new ExecutionContext { CorrelationId = Guid.NewGuid().ToString() };
}
