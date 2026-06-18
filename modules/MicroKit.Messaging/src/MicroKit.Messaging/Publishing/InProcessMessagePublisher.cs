namespace MicroKit.Messaging.Publishing;

using MicroKit.Messaging.Registry;

/// <summary>
/// In-process <see cref="IMessagePublisher"/> that writes one <see cref="InboxMessage"/>
/// per registered consumer into the transactional inbox, rather than dispatching directly
/// to a message broker. The inbox drain loop (<c>InboxProcessor</c>) then invokes the
/// handlers asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// Subscriber lookup uses <c>evt.GetType()</c> (the runtime type), never
/// <c>typeof(T)</c>, to ensure concrete subtype properties are not silently dropped
/// when the event reference is typed as <see cref="IIntegrationEvent"/>.
/// </para>
/// <para>
/// A missing subscriber is not an error — it is valid for a multi-service deployment
/// where a given event has no local consumer. A warning is logged instead.
/// </para>
/// <para>
/// This class is registered as <strong>scoped</strong> (not singleton) in the DI
/// container because it depends on <see cref="IInboxStore"/>, which is backed by a
/// scoped <c>DbContext</c> in <c>MicroKit.Messaging.EntityFrameworkCore</c>. It is
/// resolved from the per-message execution scope created by <c>OutboxProcessor</c>.
/// </para>
/// </remarks>
internal sealed class InProcessMessagePublisher : IMessagePublisher
{
    private readonly MessageHandlerRegistry _registry;
    private readonly IInboxStore _inboxStore;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<InProcessMessagePublisher> _logger;

    /// <summary>
    /// Initializes a new <see cref="InProcessMessagePublisher"/>.
    /// </summary>
    public InProcessMessagePublisher(
        MessageHandlerRegistry registry,
        IInboxStore inboxStore,
        IMessageSerializer serializer,
        ILogger<InProcessMessagePublisher> logger)
    {
        _registry = registry;
        _inboxStore = inboxStore;
        _serializer = serializer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync<T>(T evt, CancellationToken ct = default)
        where T : IIntegrationEvent
    {
        var runtimeType = evt.GetType();
        var handlers = _registry.GetHandlers(runtimeType);

        if (handlers.Count == 0)
        {
            _logger.LogWarning(
                "No subscribers registered for event type {EventType}. Message will not be delivered in-process.",
                runtimeType.AssemblyQualifiedName);
            return;
        }

        var payload = _serializer.Serialize(evt);
        var eventType = runtimeType.AssemblyQualifiedName!;

        foreach (var handler in handlers)
        {
            var message = new InboxMessage
            {
                MessageId = evt.MessageId,
                ConsumerType = handler.ConsumerType,
                TenantId = evt.TenantId,
                EventType = eventType,
                Payload = payload,
                Status = InboxMessageStatus.Received,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = evt.CorrelationId,
                CausationId = evt.CausationId,
            };

            await _inboxStore.AddAsync(message, ct).ConfigureAwait(false);
        }
    }
}
