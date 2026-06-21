namespace MicroKit.Messaging.MediatR.Outbox;

/// <summary>
/// Routing <see cref="IOutboxDispatcher"/> decorator for the MediatR transport path. Deserializes
/// each outbox row and routes by payload kind:
/// <list type="bullet">
///   <item>payload is a MediatR <see cref="INotification"/> (a domain-event notification) →
///         publish in-process via <see cref="IPublisher.Publish"/>.</item>
///   <item>otherwise (integration event, or unresolvable payload) → delegate to the wrapped
///         Core dispatcher (<c>InProcessIntegrationDispatcher</c>), preserving the
///         integration-event-via-outbox path (ADR-MSG-002).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <strong>Disjointness assumption:</strong> the <c>is INotification</c> branch is taken first and
/// is safe only because <c>IIntegrationEvent</c> and <c>IDomainEventNotification</c> are disjoint —
/// no integration event implements <see cref="INotification"/>, and no domain-event notification
/// implements <c>IIntegrationEvent</c>. If that ever changes, the routing order must be revisited.
/// </para>
/// <para>
/// <strong>Retry semantics:</strong> the whole <see cref="IPublisher.Publish"/> call is the retry
/// unit. There is no per-consumer inbox for notifications, so an outbox retry re-runs ALL
/// notification handlers for the message. Handlers on this path must be idempotent
/// (ADR-MSG-003 extended to the outbox→MediatR path by ADR-MSG-009).
/// </para>
/// <para>
/// The MediatR <see cref="INotification"/> / <see cref="IPublisher"/> usage here is the sole
/// permitted MediatR.Contracts reference in MicroKit.Messaging (ADR-MSG-009 carve-out). The Core
/// dispatcher owns the canonical "cannot deserialize" error for non-notification payloads.
/// </para>
/// </remarks>
internal sealed class MediatROutboxDispatcher(
    IOutboxDispatcher inner,
    IMessageSerializer serializer,
    IPublisher publisher,
    ILogger<MediatROutboxDispatcher> logger)
    : IOutboxDispatcher
{
    /// <inheritdoc />
    public async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        var payload = serializer.Deserialize(message.Payload, message.EventType);

        if (payload is INotification notification)
        {
            logger.LogDebug(
                "MediatROutboxDispatcher: publishing notification {MessageId} (EventType '{EventType}') via MediatR.",
                message.Id.Value,
                message.EventType);

            await publisher.Publish(notification, ct).ConfigureAwait(false);
            return;
        }

        // Integration-event path (or unresolvable payload): delegate to the Core dispatcher,
        // which deserializes/publishes and owns the canonical deserialization-failure error.
        await inner.DispatchAsync(message, ct).ConfigureAwait(false);
    }
}
