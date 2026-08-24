namespace MicroKit.Messaging;

/// <summary>
/// Dispatch seam between the payload-agnostic <c>OutboxProcessor</c> engine and the
/// transport layer. The engine passes the raw <see cref="OutboxMessage"/> row;
/// the dispatcher owns deserialization and delivery.
/// </summary>
/// <remarks>
/// <para>
/// In-process v1 default: <c>InProcessIntegrationDispatcher</c> (Core package) —
/// deserializes the payload via <c>IMessageSerializer</c> and calls
/// <c>IMessagePublisher.PublishAsync</c> to write inbox rows.
/// </para>
/// <para>
/// Broker providers (v2): replace this seam with a broker-specific implementation
/// (e.g. <c>RabbitMqOutboxDispatcher</c>) without modifying the engine. Register it as a
/// <b>scoped</b> <see cref="IOutboxDispatcher"/> from the provider's own
/// <c>Add{Provider}Transport()</c> extension; the engine resolves it from the per-message
/// execution scope. Nothing about this seam is a hosted service — <c>OutboxWorker</c> is the
/// only hosted service on the outbox path, and it is internal.
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
