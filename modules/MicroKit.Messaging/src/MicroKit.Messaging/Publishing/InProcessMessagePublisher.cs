namespace MicroKit.Messaging.Publishing;

using MicroKit.Messaging.Processing;
using MicroKit.Messaging.Registry;

/// <summary>
/// In-process <see cref="IMessagePublisher"/> that writes one <see cref="InboxMessage"/> per
/// registered consumer into the transactional inbox, rather than dispatching directly to a
/// message broker. The inbox drain loop (<c>InboxProcessor</c>) then invokes the handlers
/// asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// Subscriber lookup uses <c>evt.GetType()</c> (the runtime type), never <c>typeof(T)</c>, to
/// ensure concrete subtype properties are not silently dropped when the event reference is typed
/// as <see cref="IIntegrationEvent"/>.
/// </para>
/// <para>
/// A missing subscriber is not an error — it is valid for a multi-service deployment where a
/// given event has no local consumer. A warning is logged instead.
/// </para>
/// <para>
/// <b>A redelivery is not a failure.</b> <see cref="IInboxWriter.AddAsync"/> reports an
/// already-recorded row through its return value, and this loop treats that as a successful
/// skip and moves to the next consumer. Previously the duplicate surfaced as an exception,
/// propagated to <c>OutboxProcessor</c>, was classified a transient dispatch failure, and
/// dead-lettered a message that had been delivered correctly on the first attempt.
/// </para>
/// <para>
/// <b>And the skip must not abort the loop.</b> One event fans out to one row per consumer. When
/// the duplicate escaped as an exception it ended the whole publish, so consumers after the
/// duplicated one never got their row at all — a partial redelivery turning into permanent loss
/// for the later consumers.
/// </para>
/// <para>
/// This class is registered as <strong>scoped</strong> (not singleton) in the DI container
/// because it depends on <see cref="IInboxWriter"/>, which is backed by a scoped
/// <c>DbContext</c> in <c>MicroKit.Messaging.EntityFrameworkCore</c>. It is resolved from the
/// per-message execution scope created by <c>OutboxProcessor</c>.
/// </para>
/// </remarks>
internal sealed class InProcessMessagePublisher : IMessagePublisher
{
    private readonly MessageHandlerRegistry _registry;
    private readonly IInboxWriter _inboxWriter;
    private readonly IMessageSerializer _serializer;
    private readonly InboxMetrics _metrics;
    private readonly ILogger<InProcessMessagePublisher> _logger;

    /// <summary>
    /// Initializes a new <see cref="InProcessMessagePublisher"/>.
    /// </summary>
    /// <param name="registry">Maps an event type to its registered consumers.</param>
    /// <param name="inboxWriter">Ingestion half of the inbox store.</param>
    /// <param name="serializer">Serializes the event payload.</param>
    /// <param name="metrics">Counts insertions and deduplicated redeliveries.</param>
    /// <param name="logger">Logger.</param>
    public InProcessMessagePublisher(
        MessageHandlerRegistry registry,
        IInboxWriter inboxWriter,
        IMessageSerializer serializer,
        InboxMetrics metrics,
        ILogger<InProcessMessagePublisher> logger)
    {
        _registry = registry;
        _inboxWriter = inboxWriter;
        _serializer = serializer;
        _metrics = metrics;
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

            var result = await _inboxWriter.AddAsync(message, ct).ConfigureAwait(false);
            _metrics.Record(result, handler.ConsumerType);

            if (result is InboxWriteResult.AlreadyPresent)
            {
                // Next consumer — NOT a dispatch failure, and NOT an early return. This one
                // line is the fix: the publisher returns normally, so the outbox marks the
                // message Published instead of retrying it to death.
                InboxIngestionLogs.Deduplicated(
                    _logger, message.MessageId.Value, handler.ConsumerType);
                continue;
            }

            InboxIngestionLogs.Added(_logger, message.MessageId.Value, handler.ConsumerType);
        }
    }
}
