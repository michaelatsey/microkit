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
/// <para>
/// <b>There is a second holder, and it is not a copy of this one.</b>
/// <see cref="MicroKit.Messaging.Outbox.OriginMessageHolder"/> exists for the same structural
/// reason — a value has to be resolvable as a service to reach constructor injection — and behaves
/// differently in three ways a reader should not have to infer:
/// <list type="bullet">
///   <item><b>Different writer.</b> This one is written by
///         <see cref="PassThroughExecutionScopeFactory"/>, before the scope is handed out. That one
///         is written by <c>OutboxProcessor</c>, on the scope it received — deliberately not by the
///         factory, so a host supplying its own cannot drop the value.</item>
///   <item><b>Different override semantics.</b> This one is read <i>through</i>
///         <see cref="IExecutionContext"/>, so a host that registers its own scoped
///         <see cref="IExecutionContext"/> bypasses this holder entirely and takes hydration on
///         itself. That one is read as its own concrete internal type and cannot be displaced by
///         any host registration.</item>
///   <item><b>Different default.</b> This one always holds a context. That one defaults to
///         <see langword="null"/>, and null there is a valid, common and meaningful value.</item>
/// </list>
/// </para>
/// <para>
/// Neither is <c>AsyncLocal</c>, despite the collision between this type's name and
/// <see cref="System.Threading.ExecutionContext"/>, which is. Both are ordinary scoped DI objects;
/// nothing in this module reads an ambient value except <c>Activity.Current</c>.
/// </para>
/// </remarks>
internal sealed class ExecutionContextHolder
{
    /// <summary>Gets or sets the context for this scope.</summary>
    public IExecutionContext Context { get; set; } =
        new ExecutionContext { CorrelationId = Guid.NewGuid().ToString() };
}
