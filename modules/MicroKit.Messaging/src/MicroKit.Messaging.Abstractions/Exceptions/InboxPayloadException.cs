namespace MicroKit.Messaging;

/// <summary>
/// Thrown when an inbound message can never be processed successfully without the persisted data
/// itself changing, or without a redeployment — whether it has already been recorded as an inbox
/// row, or is being refused at ingestion before any row exists.
/// </summary>
/// <remarks>
/// <para>
/// The verdict is <b>permanent</b>: the failure is settled at the first attempt and no number of
/// retries can change it. Where the row exists, the processor dead-letters it immediately without
/// consuming the retry budget. Where it does not — the ingestion case below — the verdict has
/// nowhere to live but the broker, so <see cref="IEnvelopeReceiver.ReceiveAsync"/> raises this and
/// a provider must dead-letter rather than nack for retry.
/// </para>
/// <para>
/// <b>Raised by the processor for:</b> a <see cref="InboxMessage.ConsumerType"/> absent from
/// the handler registry, and a payload that fails to deserialize, deserializes to
/// <see langword="null"/>, or deserializes to something that is not an
/// <see cref="IIntegrationEvent"/>. All of those were previously retried with exponential
/// back-off, which no amount of waiting could ever resolve.
/// </para>
/// <para>
/// <b>Raised by the receiving seam for:</b> a <see cref="MessageEnvelope.ContractName"/> bound to
/// no local type in this process. The binding is established at composition, so it cannot appear
/// without a redeployment — the same permanence, reached one step earlier, before any row was
/// written.
/// </para>
/// <para>
/// <b>⚠ That permanence is a property of a process and is acted on by a fleet.</b> During a
/// rolling deployment both versions consume the same queue, so a message carrying a contract only
/// the new version binds can land on an old instance, which refuses it here and has the provider
/// dead-letter a message the instance beside it would have accepted. The classification stays —
/// it is correct for a single-version deployment, and the alternative, nacking, loops the message
/// until the broker's own limit discards it with no record of the refusal. What follows is
/// operational rather than a code change: deploy a consumer that binds a new contract
/// <i>before</i> anything publishes it, and treat this dead-letter queue as requeueable once the
/// rollout completes. A broker provider inherits this rule verbatim and should say so in its own
/// docs.
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
