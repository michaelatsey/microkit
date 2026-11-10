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
    /// <exception cref="ArgumentException">
    /// A required member of the envelope — <c>Id</c>, <c>Source</c>, <c>Type</c>,
    /// <c>DataContentType</c> or <c>Headers</c> — is missing, so the malformed envelope fails at the
    /// sender rather than in the consume loop.
    /// </exception>
    public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(envelope);
        return broker.EnqueueAsync(envelope, ct);
    }

    private static void Validate(MessageEnvelope envelope)
    {
        // `required` members are a compile-time promise only: `null!` and deserializers bypass it.
        Require(envelope.Id, nameof(MessageEnvelope.Id), nameof(envelope));
        Require(envelope.Source, nameof(MessageEnvelope.Source), nameof(envelope));
        Require(envelope.Type, nameof(MessageEnvelope.Type), nameof(envelope));
        Require(envelope.DataContentType, nameof(MessageEnvelope.DataContentType), nameof(envelope));

        if (envelope.Headers is null)
        {
            throw new ArgumentException(
                $"The envelope's {nameof(MessageEnvelope.Headers)} is null. Use an empty dictionary.",
                nameof(envelope));
        }
    }

    private static void Require(string? value, string member, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"The envelope's {member} is missing. Every envelope carries an id, a source, a type " +
                "and a content type.",
                paramName);
        }
    }
}
