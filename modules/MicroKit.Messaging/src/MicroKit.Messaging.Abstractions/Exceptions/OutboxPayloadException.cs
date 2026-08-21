namespace MicroKit.Messaging;

/// <summary>
/// Thrown by an <see cref="IOutboxDispatcher"/> when a message can never be dispatched
/// successfully without the persisted row itself being changed.
/// </summary>
/// <remarks>
/// <para>
/// The processor dead-letters the message immediately, without consuming the retry
/// budget. Retrying a poison message <c>MaxRetries</c> times spends a dispatch, an
/// execution scope and a write on each attempt for a verdict already reached on the first.
/// </para>
/// <para>
/// <b>Throw for:</b> an <see cref="OutboxMessage.EventType"/> that resolves to no CLR type,
/// an <see cref="OutboxMessage.Payload"/> that is not valid JSON, a payload that does not
/// match the event's schema, or a contract the transport rejects outright.
/// </para>
/// <para>
/// <b>Never throw for:</b> broker nack, timeout, connection refused, HTTP 503, database
/// timeout. Those are infrastructure faults and must stay transient — dead-lettering
/// them discards a message that would have succeeded a second later.
/// </para>
/// <para>
/// The classification is deliberately conservative: only failures MicroKit can prove
/// permanent are dead-lettered on sight; everything unrecognised is treated as transient.
/// A library that guesses wrong in this direction loses messages.
/// </para>
/// </remarks>
public sealed class OutboxPayloadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OutboxPayloadException"/> class.</summary>
    public OutboxPayloadException()
        : base("The outbox message payload is permanently undeliverable.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboxPayloadException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public OutboxPayloadException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboxPayloadException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying deserialization or contract fault.</param>
    public OutboxPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
