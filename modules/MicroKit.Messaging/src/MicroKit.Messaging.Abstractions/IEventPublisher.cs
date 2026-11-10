namespace MicroKit.Messaging;

/// <summary>
/// Publishes integration events onto the bus. The narrow contract for code that only publishes;
/// <see cref="IMessageBus"/> extends it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without durability, a publication goes straight to the transport.</b> No database, no
/// transaction, no outbox row: <see cref="PublishAsync{T}"/> returns once the transport has
/// accepted the message. Opt-in durability through the outbox is #188.
/// </para>
/// <para>
/// <b>Ambient context is read from the current scope's execution context</b>, never from an
/// <c>AsyncLocal</c>. Inside a message handler that scope is the message's own, so a message
/// published while handling another inherits its tenant and correlation, and names the incoming
/// message as its causation. Outside a message the ambient tenant is whatever the scope's
/// execution context holds — null by default — and <see cref="PublishOptions.TenantId"/> supplies
/// one explicitly.
/// </para>
/// <para>
/// Registered as a scoped service. Resolve it from the scope whose context should apply.
/// </para>
/// </remarks>
public interface IEventPublisher
{
    /// <summary>Publishes one integration event.</summary>
    /// <typeparam name="T">
    /// The event type. Must be registered as a published contract and carry
    /// <see cref="MessageContractAttribute"/>.
    /// </typeparam>
    /// <param name="message">The event. Business payload only.</param>
    /// <param name="options">
    /// Optional publication settings: an explicit tenant and extra headers.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The identifier assigned to the published message.</returns>
    /// <exception cref="IntegrationEventConfigurationException">
    /// The event type is not registered, or no transport is registered.
    /// </exception>
    /// <exception cref="IntegrationEventPublishException">
    /// <see cref="PublishOptions.TenantId"/> contradicts a non-null ambient tenant. Nothing is sent.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="PublishOptions.Headers"/> names a reserved header.
    /// </exception>
    ValueTask<MessageId> PublishAsync<T>(
        T message,
        PublishOptions? options = null,
        CancellationToken ct = default)
        where T : class, IIntegrationEvent;
}
