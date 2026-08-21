namespace MicroKit.Messaging.Dispatch;

/// <summary>
/// <see cref="IOutboxDispatcher"/> implementation that routes a dispatched
/// <see cref="OutboxMessage"/> to the in-process <see cref="IMessagePublisher"/>.
/// Deserializes the JSON payload back to its concrete <see cref="IIntegrationEvent"/>
/// type and delegates to the publisher, which writes one <see cref="InboxMessage"/>
/// row per registered consumer.
/// </summary>
/// <remarks>
/// This class is registered as <strong>scoped</strong> (not singleton) because it
/// depends on <see cref="IMessagePublisher"/> (<c>InProcessMessagePublisher</c>), which
/// is itself scoped due to its <see cref="IInboxStore"/> dependency. Both are resolved
/// from the per-message execution scope created by <c>OutboxProcessor</c>.
/// </remarks>
internal sealed class InProcessIntegrationDispatcher : IOutboxDispatcher
{
    private readonly IMessageSerializer _serializer;
    private readonly IMessagePublisher _publisher;

    /// <summary>
    /// Initializes a new <see cref="InProcessIntegrationDispatcher"/>.
    /// </summary>
    public InProcessIntegrationDispatcher(IMessageSerializer serializer, IMessagePublisher publisher)
    {
        _serializer = serializer;
        _publisher = publisher;
    }

    /// <inheritdoc />
    /// <exception cref="OutboxPayloadException">
    /// The payload can never be dispatched without the persisted row itself changing: the
    /// <see cref="OutboxMessage.EventType"/> resolves to no CLR type, the
    /// <see cref="OutboxMessage.Payload"/> is not valid JSON, or the resolved type is not an
    /// <see cref="IIntegrationEvent"/>. All three are proven permanent, so the processor
    /// dead-letters on first sight instead of spending the whole retry budget re-reaching the
    /// same verdict.
    /// </exception>
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        // IMessageSerializer.Deserialize never throws: it returns null when the EventType does
        // not resolve or the JSON is malformed. The `as` then also yields null when the type
        // resolved but is not a transport contract. Those are the only three cases here, and
        // every one of them is permanent — nothing infrastructural (a nack, a timeout, a
        // refused connection, an HTTP 503, a database timeout) can reach this branch, which is
        // exactly the exclusion rule OutboxPayloadException documents.
        var evt = _serializer.Deserialize(message.Payload, message.EventType) as IIntegrationEvent;
        if (evt is null)
            throw new OutboxPayloadException(
                $"Cannot deserialize EventType '{message.EventType}' from outbox message {message.Id} " +
                "as an IIntegrationEvent. Ensure the event type is resolvable in the current assembly " +
                "context and implements IIntegrationEvent.");

        // Anything the publisher throws stays untyped, and therefore transient. A duplicate
        // inbox row surfaces here as a DbUpdateException and must keep being retried.
        await _publisher.PublishAsync(evt, ct).ConfigureAwait(false);
    }
}
