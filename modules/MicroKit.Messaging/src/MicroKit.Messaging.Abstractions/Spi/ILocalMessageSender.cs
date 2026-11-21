namespace MicroKit.Messaging.Spi;

/// <summary>
/// Sends a local message: an in-process message delivered to the local consumers of its type,
/// never handed to <see cref="IMessageTransport"/> (#188 D7).
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI: an integration package — <c>MicroKit.Messaging.MediatR</c> for domain-event
/// notifications — sends through it; application code publishes through
/// <see cref="IMessageBus"/>.
/// </para>
/// <para>
/// <b>With a store</b>, the message is enlisted as an outbox row routed to the local endpoint
/// (<see cref="OutboxMessage.LocalEndpoint"/>), in the store the bus would choose for a publication
/// of its type: the delivery's store inside a delivery, the only store, or the store bound to the
/// type's assembly. Nothing is saved: the row commits with whatever saves that store's unit of work,
/// and a row still unsaved when its scope ends is reported as lost. The store's outbox loop then
/// delivers it once per consumer, each consumer guarded by its own inbox row.
/// </para>
/// <para>
/// <b>Without a store</b>, the message is queued in memory at once, one entry per consumer, and
/// delivered by a worker of its own, one scope per consumer: each consumer has its own in-memory
/// retries and dead letter, so one that failed is redelivered alone and one that succeeded never runs
/// again. It is queued before the sender's work commits: it is delivered even if that work rolls back,
/// and lost if the process stops. A message no local consumer handles is
/// not queued: it is reported (a <c>Warning</c>, and the unconsumed counter), as an arriving message
/// with no handler is.
/// </para>
/// <para>
/// No <see cref="MessageContractAttribute"/> is needed: a local message never leaves the process.
/// A consumer never runs in the sender's scope. Scoped: resolve it from the scope whose unit of
/// work the message belongs to.
/// </para>
/// </remarks>
public interface ILocalMessageSender
{
    /// <summary>Sends <paramref name="message"/> to the local consumers of its runtime type.</summary>
    /// <param name="message">The message; serialized, so each consumer receives its own copy.</param>
    /// <param name="messageId">The message's identity, end to end: the inbox key of each consumer.</param>
    /// <param name="occurredOnUtc">When the fact the message announces happened.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes once the message is enlisted or queued.</returns>
    /// <exception cref="IntegrationEventConfigurationException">Several stores are configured and
    /// none can be chosen for the message's type.</exception>
    ValueTask SendAsync(
        object message, MessageId messageId, DateTimeOffset occurredOnUtc, CancellationToken ct = default);
}
