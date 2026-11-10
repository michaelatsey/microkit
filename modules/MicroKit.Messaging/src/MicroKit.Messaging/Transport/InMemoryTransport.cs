namespace MicroKit.Messaging.Transport;

/// <summary>
/// The in-memory <see cref="IMessageTransport"/>: enqueues on the <see cref="InMemoryBroker"/>.
/// </summary>
/// <remarks>
/// Returning means the broker accepted the envelope — the queue holds it, and
/// <see cref="InMemoryTransportWorker"/> delivers it. Unlike a transport with no receiver, this
/// one is never a silent success: the consume loop is registered by the same call that registers
/// it, <see cref="MessagingBuilder.UseInMemoryTransport"/>.
/// </remarks>
internal sealed class InMemoryTransport(InMemoryBroker broker) : IMessageTransport
{
    /// <inheritdoc />
    public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return broker.EnqueueAsync(envelope, ct);
    }
}
