using MicroKit.Messaging.Outbox;

namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// Production implementation of <see cref="IDomainEventDispatcher"/>.
/// Drains domain events from the aggregate provider, dispatches them synchronously to all
/// registered <see cref="IDomainEventHandler{TEvent}"/> implementations within the current
/// transaction scope (P3), then writes each notification to the transactional outbox for
/// asynchronous fan-out via the outbox processor (P4).
/// </summary>
/// <remarks>
/// <para>
/// Single drain pass per dispatch — four phases:
/// <list type="number">
///   <item>P1 — drain accumulated domain events from tracked aggregates.</item>
///   <item>P2 — skip non-<see cref="IEvent"/> domain events (no MediatR mapping).</item>
///   <item>P3 — invoke all registered <see cref="IDomainEventHandler{TEvent}"/> synchronously
///         via <see cref="IDomainEventHandlerDispatcher"/>. Handlers share the same unit-of-work
///         scope as the aggregate save. Fires for every <see cref="IEvent"/> regardless of
///         whether a notification exists.</item>
///   <item>P4 — build the MediatR notification via <see cref="INotificationFactory"/> and write
///         it to the transactional outbox (skipped if the factory returns <see langword="null"/>
///         for this event type). Payload = serialized notification.
///         <c>MessageId</c>/<c>OccurredOnUtc</c> come from the event's intrinsic properties;
///         transit metadata (<c>TenantId</c>/<c>CorrelationId</c>/<c>CausationId</c>) from
///         <see cref="IExecutionContext"/>, never from the payload.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Handler disjointness:</strong> <see cref="IDomainEventHandler{TEvent}"/> (P3)
/// and <see cref="INotificationHandler{TNotification}"/> (outbox relay, P4+) are structurally
/// disjoint dispatch paths. P3 handlers run in-transaction; notification handlers run
/// asynchronously via the outbox processor after the transaction commits. Because an outbox
/// retry re-publishes the notification and re-runs ALL of its notification handlers, those
/// handlers must be idempotent (ADR-MSG-003 / ADR-MSG-009).
/// </para>
/// </remarks>
internal sealed class DomainEventsDispatcher(
    IDomainEventsProvider eventsProvider,
    IDomainEventHandlerDispatcher handlerDispatcher,
    INotificationFactory notificationFactory,
    OutboxMessageFactory outboxFactory,
    IOutboxWriter outboxWriter,
    IExecutionContext executionContext)
    : IDomainEventDispatcher
{
    /// <inheritdoc />
    public async Task DispatchEventsAsync(CancellationToken ct = default)
    {
        // P1 — drain all events currently accumulated on tracked aggregates.
        var domainEvents = eventsProvider.DrainDomainEvents();
        if (domainEvents.Count == 0) return;

        foreach (var domainEvent in domainEvents)
        {
            // P2 — skip non-IEvent domain events (no MediatR mapping).
            if (domainEvent is not IEvent mediatREvent) continue;

            // P3 — synchronous in-transaction dispatch to IDomainEventHandler<TEvent> implementations.
            await handlerDispatcher.DispatchAsync(mediatREvent, ct).ConfigureAwait(false);

            // P4 — build the MediatR notification; write to outbox (skip if no notification registered).
            var notification = notificationFactory.Create(mediatREvent);
            if (notification is null) continue;

            // messageId / occurredOnUtc sourced from IDomainEvent intrinsic properties; transit
            // metadata sourced from IExecutionContext columns, not from the payload.
            var outboxMessage = outboxFactory.Create(
                notification,
                domainEvent.EventId,     // IDomainEvent.EventId (Guid) — stable end-to-end id
                domainEvent.OccurredAt,  // IDomainEvent.OccurredAt (DateTimeOffset)
                executionContext);

            await outboxWriter.AddAsync(outboxMessage, ct).ConfigureAwait(false);
        }
    }
}
