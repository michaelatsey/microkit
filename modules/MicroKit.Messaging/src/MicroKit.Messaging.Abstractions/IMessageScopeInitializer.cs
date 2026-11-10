namespace MicroKit.Messaging;

/// <summary>
/// Prepares a message scope after it is created and before any service of that scope — the
/// handler, its <c>DbContext</c>, the receive middleware — is resolved.
/// </summary>
/// <remarks>
/// <para>
/// The stage exists so an adapter can establish context that later services read at construction:
/// typically a tenant, so that a tenant-aware <c>DbContext</c> is configured for the right tenant
/// (#189, #190). The scope's execution context is already filled from the envelope when this runs.
/// </para>
/// <para>
/// Initializers run in registration order, once per handler scope, and are resolved from that
/// scope. Keep their own constructor dependencies free of the services they prepare. An exception
/// is classified like a handler's: <see cref="PermanentMessageException"/> dead-letters,
/// <see cref="InboxDependencyUnavailableException"/> pauses the endpoint, anything else redelivers.
/// </para>
/// </remarks>
public interface IMessageScopeInitializer
{
    /// <summary>Prepares the scope for one message.</summary>
    /// <param name="context">The message being delivered.</param>
    /// <param name="services">The message scope's service provider.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the scope is ready.</returns>
    ValueTask InitializeAsync(
        MessageContext context,
        IServiceProvider services,
        CancellationToken ct = default);
}
