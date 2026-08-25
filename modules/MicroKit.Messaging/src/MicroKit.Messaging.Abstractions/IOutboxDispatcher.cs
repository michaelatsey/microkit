namespace MicroKit.Messaging;

/// <summary>
/// Dispatch seam between the payload-agnostic <c>OutboxProcessor</c> engine and the
/// transport layer. The engine passes the raw <see cref="OutboxMessage"/> row;
/// the dispatcher owns deserialization and delivery.
/// </summary>
/// <remarks>
/// <para>
/// Core ships two implementations, and which one is registered decides what the outbox does with a
/// row:
/// <list type="bullet">
///   <item><c>TransportOutboxDispatcher</c> (<c>AddTransportDispatcher()</c>) — routes a
///         <see cref="MessageKind.Contract"/> row to <see cref="IMessageTransport"/> as a
///         <see cref="MessageEnvelope"/>. It does not deserialize: the payload travels
///         opaque.</item>
///   <item><c>InProcessIntegrationDispatcher</c> (<c>AddInProcessTransport()</c>) — deserializes
///         the payload to find its registered consumers and writes one inbox row per
///         consumer.</item>
/// </list>
/// </para>
/// <para>
/// <b>A broker provider does not implement this seam.</b> It implements
/// <see cref="IMessageTransport"/> and registers it from its own <c>Add{Provider}Transport()</c>
/// extension; <c>TransportOutboxDispatcher</c> is what feeds it. Nothing about either seam is a
/// hosted service — <c>OutboxWorker</c> is the only hosted service on the outbox path, and it is
/// internal.
/// </para>
/// <para>
/// Implementations are registered as <b>scoped</b>, with <c>TryAdd</c>: the engine resolves one
/// from the per-message execution scope, and a plain <c>Add</c> would append a second descriptor
/// that Microsoft DI resolves in preference — silently bypassing any decorator over this seam.
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
    /// the full row — dispatchers may read <see cref="OutboxMessage.EventType"/>,
    /// <see cref="OutboxMessage.Payload"/>, <see cref="OutboxMessage.TenantId"/>,
    /// and any correlation fields.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the message has been
    /// handed off to the transport. Any exception propagates to the engine, which
    /// routes the message to retry or dead-letter based on <c>OutboxProcessorOptions</c>.</returns>
    ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default);
}
