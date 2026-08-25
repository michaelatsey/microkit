namespace MicroKit.Messaging;

/// <summary>
/// Dispatch seam between the payload-agnostic <c>OutboxProcessor</c> engine and the
/// transport layer. The engine passes the raw <see cref="OutboxMessage"/> row;
/// the dispatcher owns delivery, and deserialization only where delivery needs it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Routing is decided by <see cref="OutboxMessage.MessageKind"/>, never by the payload's CLR
/// type.</b> The outbox is reentrant — one table carrying notifications and contracts — and the
/// column is what distinguishes them. A type test would be invisible to SQL and would force this
/// payload-agnostic seam to know about abstractions it deliberately does not reference.
/// </para>
/// <para>
/// Core ships one implementation: <c>TransportOutboxDispatcher</c>
/// (<c>MessagingBuilder.AddTransportDispatcher()</c>), which turns a
/// <see cref="MessageKind.Contract"/> row into a <see cref="MessageEnvelope"/> and hands it to
/// <see cref="IMessageTransport"/>. It does not deserialize: the payload travels opaque.
/// <c>MicroKit.Messaging.MediatR</c> adds a decorator that serves
/// <see cref="MessageKind.Notification"/> rows in process and delegates everything else inward.
/// </para>
/// <para>
/// <b>A broker provider does not implement this seam.</b> It implements
/// <see cref="IMessageTransport"/> and registers it from its own <c>Add{Provider}Transport()</c>
/// extension; <c>TransportOutboxDispatcher</c> is what feeds it. Nothing about either seam is a
/// hosted service — <c>OutboxWorker</c> is the only hosted service on the outbox path, and it is
/// internal.
/// </para>
/// <para>
/// <b>Registration uses two slots, and the split is the contract.</b> Implementations are
/// <b>scoped</b> — the engine resolves one from the per-message execution scope. The standard
/// dispatcher is registered under the <see cref="OutboxDispatcherKeys.Standard"/> key, written only
/// by Core; the unkeyed <see cref="IOutboxDispatcher"/> registration is the slot the engine
/// resolves, and a decorating package removes whatever holds it and takes it outright. A decorator
/// finds its inner through the key, so neither package has to be registered before the other. See
/// <see cref="OutboxDispatcherKeys"/> for what happens when both write one slot instead.
/// </para>
/// <para>
/// A decorator must treat its inner as <b>optional</b>. A host that publishes only domain-event
/// notifications registers no transport dispatcher at all, and that is a legal composition: the
/// keyed lookup yields <see langword="null"/>, notifications dispatch, and a
/// <see cref="MessageKind.Contract"/> row raises <see cref="OutboxConfigurationException"/> rather
/// than being silently discarded.
/// </para>
/// <para>
/// The message handed over is payload-agnostic. Implementations must not assume the row holds
/// an <see cref="IIntegrationEvent"/>: see <see cref="OutboxMessage.EventType"/> for what it
/// may actually carry.
/// </para>
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Dispatches a single outbox message to its target transport.
    /// Implementations own deserialization of <see cref="OutboxMessage.Payload"/>
    /// and any transport-level acknowledgement.
    /// </summary>
    /// <param name="message">The outbox message to dispatch. The engine provides
    /// the full row — dispatchers read <see cref="OutboxMessage.MessageKind"/> to route, and may
    /// read <see cref="OutboxMessage.EventType"/>, <see cref="OutboxMessage.Payload"/>,
    /// <see cref="OutboxMessage.TenantId"/>, and any correlation fields.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the message has been
    /// handed off to the transport. Any exception propagates to the engine, which
    /// routes the message to retry or dead-letter based on <c>OutboxProcessorOptions</c>.</returns>
    ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default);
}
