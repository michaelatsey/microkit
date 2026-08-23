namespace MicroKit.Messaging;

/// <summary>
/// Thrown when an inbox row can never be processed successfully without the persisted data
/// itself changing, or without a redeployment.
/// </summary>
/// <remarks>
/// <para>
/// The processor dead-letters the row immediately, without consuming the retry budget.
/// Retrying a poison message <c>MaxRetries</c> times spends an execution scope and a write on
/// each attempt for a verdict already reached at the first.
/// </para>
/// <para>
/// <b>Raised by the processor for:</b> a <see cref="InboxMessage.ConsumerType"/> absent from
/// the handler registry, and a payload that fails to deserialize, deserializes to
/// <see langword="null"/>, or deserializes to something that is not an
/// <see cref="IIntegrationEvent"/>. All of those were previously retried with exponential
/// back-off, which no amount of waiting could ever resolve.
/// </para>
/// <para>
/// <b>May also be thrown by a handler for:</b> a payload that parses but violates the event
/// contract.
/// </para>
/// <para>
/// <b>Never throw for:</b> timeout, connection refused, HTTP 503, database timeout, deadlock.
/// Those are infrastructure faults and must stay transient — dead-lettering them discards a
/// message that would have succeeded a second later. The classification is deliberately
/// conservative: only proven permanence dead-letters, because a library that guesses wrongly
/// in this direction loses messages.
/// </para>
/// </remarks>
public sealed class InboxPayloadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="InboxPayloadException"/> class.</summary>
    public InboxPayloadException()
        : base("The inbox message payload is permanently unprocessable.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InboxPayloadException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public InboxPayloadException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InboxPayloadException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying deserialization or contract fault.</param>
    public InboxPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
