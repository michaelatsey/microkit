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
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        var evt = _serializer.Deserialize(message.Payload, message.EventType);
        if (evt is null)
            throw new InvalidOperationException(
                $"Cannot deserialize EventType '{message.EventType}' from outbox message {message.Id}. " +
                "Ensure the event type is resolvable in the current assembly context.");

        await _publisher.PublishAsync(evt, ct).ConfigureAwait(false);
    }
}
