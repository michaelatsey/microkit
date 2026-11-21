namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// The <see cref="IDomainEventsSink"/> of <c>MicroKit.Messaging.MediatR</c>: sends each drained
/// domain event's notification as a local message (#188 D8).
/// </summary>
/// <remarks>
/// <para>
/// P3 and P4 of the dispatch topology: the core orchestrator has drained the events and run every
/// <see cref="IDomainEventHandler{TEvent}"/> before any sink, so no P2 handler observes a partial
/// batch (ADR-MEDIATR-014). Each event with a registered notification becomes one local message,
/// identified by the event's <see cref="IDomainEvent.EventId"/> — every consumer's inbox key — and
/// dated by its <see cref="IDomainEvent.OccurredAt"/>. Tenant, correlation and causation come from
/// the scope.
/// </para>
/// <para>
/// <b>Enlisted, never saved.</b> With a store, <see cref="ILocalMessageSender"/> adds the row to the
/// unit of work of the store the bus would choose for the notification's type — the delivery's
/// store inside a delivery, the only store, or the store bound to the notification type's assembly —
/// and the caller's save commits it with the aggregate. With several stores, that store's context is
/// the one the unit of work must save: a row enlisted in another store's context is reported as
/// lost when the scope ends. Without a store the message is queued in memory at once.
/// </para>
/// </remarks>
internal sealed class DomainEventNotificationSink(
    IDomainEventNotificationFactory notifications,
    ILocalMessageSender sender)
    : IDomainEventsSink
{
    /// <inheritdoc />
    public async ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        for (var i = 0; i < domainEvents.Count; i++)
        {
            var domainEvent = domainEvents[i];
            if (notifications.Create(domainEvent) is { } notification)
            {
                await sender.SendAsync(notification, MessageId.From(domainEvent.EventId), domainEvent.OccurredAt, ct)
                    .ConfigureAwait(false);
            }
        }
    }
}
