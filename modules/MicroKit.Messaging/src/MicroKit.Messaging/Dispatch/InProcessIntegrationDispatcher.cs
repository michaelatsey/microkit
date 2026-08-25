namespace MicroKit.Messaging.Dispatch;

using MicroKit.Messaging.Processing;
using MicroKit.Messaging.Registry;

/// <summary>
/// <see cref="IOutboxDispatcher"/> implementation that delivers an integration event in-process by
/// writing one <see cref="InboxMessage"/> row per registered consumer. The inbox drain loop
/// (<c>InboxProcessor</c>) then invokes the handlers asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fan-out used to live behind <c>IMessagePublisher</c>, and that seam was the defect.</b>
/// A publisher receives an event and nothing else, so it had to reconstruct the message metadata
/// by reading <c>MessageId</c>, <c>TenantId</c>, <c>CorrelationId</c> and <c>CausationId</c> off
/// the event instance — which is the only reason <c>IIntegrationEvent</c> carried those members at
/// all. This class already holds the <see cref="OutboxMessage"/>, where every one of those fields
/// lives as a column, so the seam threw away the authoritative source and then obliged the
/// contract to carry a copy of it. Removing the seam removes the reason for the copy
/// (ADR-MSG-018).
/// </para>
/// <para>
/// <b>The dedup key is the outbox row's <see cref="OutboxMessage.Id"/>.</b> That is what the inbox
/// deduplicates on, together with the consumer type, and it must be stable across redeliveries of
/// the same row. Reading it off the deserialized event only ever survived retries by accident:
/// deserializing the same payload happens to produce the same value, but nothing guaranteed it —
/// not the contract, not the serializer, and certainly not an event type free to compute its
/// identity in a property initializer. Sourcing it from the row makes the guarantee structural.
/// </para>
/// <para>
/// <b>This is not a transport and must not become one.</b> It writes inbox rows because that is
/// what the in-process path does today. A real transport — an envelope, a broker, an
/// <c>IMessageTransport</c> seam — is a later lot, and nothing here should be generalised in
/// anticipation of it.
/// </para>
/// <para>
/// <b>A redelivery is not a failure.</b> <see cref="IInboxWriter.AddAsync"/> reports an
/// already-recorded row through its return value; this loop counts it, logs it at <c>Debug</c>,
/// and moves to the next consumer. Both halves matter: returning normally is what lets the outbox
/// mark the message <c>Published</c> instead of retrying it to death, and continuing rather than
/// returning early is what stops a partial redelivery from becoming permanent loss for the
/// consumers after the duplicated one.
/// </para>
/// <para>
/// A missing subscriber is not an error either — it is valid for a multi-service deployment where
/// an event has no local consumer. A warning is logged instead.
/// </para>
/// <para>
/// Registered as <strong>scoped</strong>: it depends on <see cref="IInboxWriter"/>, which is backed
/// by a scoped <c>DbContext</c> in <c>MicroKit.Messaging.EntityFrameworkCore</c>. It is resolved
/// from the per-message execution scope created by <c>OutboxProcessor</c>.
/// </para>
/// </remarks>
internal sealed class InProcessIntegrationDispatcher : IOutboxDispatcher
{
    private readonly IMessageSerializer _serializer;
    private readonly MessageHandlerRegistry _registry;
    private readonly IInboxWriter _inboxWriter;
    private readonly InboxMetrics _metrics;
    private readonly ILogger<InProcessIntegrationDispatcher> _logger;

    /// <summary>
    /// Initializes a new <see cref="InProcessIntegrationDispatcher"/>.
    /// </summary>
    /// <param name="serializer">Resolves the payload's runtime type for the consumer lookup.</param>
    /// <param name="registry">Maps an event type to its registered consumers.</param>
    /// <param name="inboxWriter">Ingestion half of the inbox store.</param>
    /// <param name="metrics">Counts insertions and deduplicated redeliveries.</param>
    /// <param name="logger">Logger.</param>
    public InProcessIntegrationDispatcher(
        IMessageSerializer serializer,
        MessageHandlerRegistry registry,
        IInboxWriter inboxWriter,
        InboxMetrics metrics,
        ILogger<InProcessIntegrationDispatcher> logger)
    {
        _serializer = serializer;
        _registry = registry;
        _inboxWriter = inboxWriter;
        _metrics = metrics;
        _logger = logger;
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
        //
        // The deserialized instance is used for ONE thing: its runtime type, which is the
        // consumer lookup key. Every value written to the inbox row comes from the outbox row.
        var evt = _serializer.Deserialize(message.Payload, message.EventType) as IIntegrationEvent;
        if (evt is null)
            throw new OutboxPayloadException(
                $"Cannot deserialize EventType '{message.EventType}' from outbox message {message.Id} " +
                "as an IIntegrationEvent. Ensure the event type is resolvable in the current assembly " +
                "context and implements IIntegrationEvent.");

        // evt.GetType(), never typeof(T): a subscriber is registered against the concrete event
        // type, and the static type here is always the interface.
        var handlers = _registry.GetHandlers(evt.GetType());

        if (handlers.Count == 0)
        {
            _logger.LogWarning(
                "No subscribers registered for event type {EventType}. Message will not be delivered in-process.",
                message.EventType);
            return;
        }

        foreach (var handler in handlers)
        {
            var inboxMessage = new InboxMessage
            {
                // Every field from the outbox row. See the class remarks: the row is the
                // authoritative source, and MessageId in particular must be stable across
                // redeliveries for the inbox unique index to deduplicate them.
                MessageId = message.Id,
                ConsumerType = handler.ConsumerType,
                TenantId = message.TenantId,
                EventType = message.EventType,
                Payload = message.Payload,
                Status = InboxMessageStatus.Received,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = message.CorrelationId,
                CausationId = message.CausationId,
            };

            var result = await _inboxWriter.AddAsync(inboxMessage, ct).ConfigureAwait(false);
            _metrics.Record(result, handler.ConsumerType);

            if (result is InboxWriteResult.AlreadyPresent)
            {
                // Next consumer — NOT a dispatch failure, and NOT an early return.
                InboxIngestionLogs.Deduplicated(
                    _logger, inboxMessage.MessageId.Value, handler.ConsumerType);
                continue;
            }

            InboxIngestionLogs.Added(_logger, inboxMessage.MessageId.Value, handler.ConsumerType);
        }
    }
}
