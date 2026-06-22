using MicroKit.Messaging.Outbox;

namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// Production implementation of <see cref="IDomainEventsDispatcher"/> for the Messaging.MediatR glue.
/// Drains domain events from the aggregate provider and runs the full four-phase dispatch:
/// P1 drain → P2 synchronous handler dispatch → P3 notification creation → P4 batch outbox write.
/// </summary>
/// <remarks>
/// <para>
/// Single drain pass per dispatch call — four sequential phases:
/// <list type="number">
///   <item>P1 — drain all domain events accumulated on tracked aggregates via <see cref="IDomainEventsProvider"/>.</item>
///   <item>P2 — invoke ALL registered <see cref="IDomainEventHandler{TEvent}"/> for EVERY event before any
///         notification work begins. Handlers run synchronously in-transaction, sharing the same
///         unit-of-work scope as the aggregate save. Domain events and their handlers are distinct
///         from notifications — handlers receive the raw <c>IDomainEvent</c>, never the outbox wrapper.</item>
///   <item>P3 — for each domain event, build the <see cref="IDomainEventNotification{TEventType}"/>
///         wrapper via <see cref="IDomainEventNotificationFactory"/>. Events with no registered
///         notification type produce <see langword="null"/> and are skipped at P4.
///         Notifications are disjoint from P2 handlers: a notification handler (<see cref="INotificationHandler{TNotification}"/>)
///         receives the <em>notification</em>, never the raw event.</item>
///   <item>P4 — write all notifications to the transactional outbox in a single batch
///         (<see cref="IOutboxWriter.AddBatchAsync"/>). One DB round-trip regardless of event count.
///         <c>MessageId</c>/<c>OccurredOnUtc</c> are sourced from the event's intrinsic properties;
///         transit metadata (<c>TenantId</c>/<c>CorrelationId</c>/<c>CausationId</c>) from
///         <see cref="IExecutionContext"/>, never from the payload.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Two-foreach design:</strong> P2 runs to completion for ALL events before P3/P4 begin.
/// This ensures that P2 handlers cannot observe a partially written outbox batch, and that any
/// domain state changes made by P2 handlers are visible before notifications are serialized.
/// </para>
/// <para>
/// <strong>Handler disjointness:</strong> <see cref="IDomainEventHandler{TEvent}"/> (P2) and
/// <see cref="INotificationHandler{TNotification}"/> (outbox relay, post-commit) are structurally
/// disjoint dispatch paths. P2 handlers run in-transaction; notification handlers run asynchronously
/// via the outbox processor after the transaction commits. Because an outbox retry re-publishes the
/// notification and re-runs ALL of its notification handlers, those handlers must be idempotent
/// (ADR-MSG-003 / ADR-MSG-009).
/// </para>
/// </remarks>
internal sealed class DomainEventsDispatcher(
    IDomainEventsProvider eventsProvider,
    IDomainEventHandlerDispatcher handlerDispatcher,
    IDomainEventNotificationFactory notificationFactory,
    OutboxMessageFactory outboxFactory,
    IOutboxWriter outboxWriter,
    IExecutionContext executionContext)
    : IDomainEventsDispatcher
{
    /// <inheritdoc />
    public async Task DispatchEventsAsync(CancellationToken ct = default)
    {
        // P1 — drain all events currently accumulated on tracked aggregates.
        var domainEvents = eventsProvider.DrainDomainEvents();
        if (domainEvents.Count == 0) return;

        // P2 — synchronous in-transaction dispatch to ALL IDomainEventHandler<TEvent> implementations.
        // All P2 handlers complete for every event before any notification work begins.
        // P2 handlers operate on the raw IDomainEvent — they are disjoint from notification handlers.
        foreach (var domainEvent in domainEvents)
            await handlerDispatcher.DispatchAsync(domainEvent, ct).ConfigureAwait(false);

        // P3 — collect IDomainEventNotification<TEvent> wrappers for events that have one registered.
        // Each domain event maps to at most one notification type (ADR-MEDIATR-005).
        // Notifications are the outbox payload — distinct types from the raw domain events above.
        var outboxMessages = new List<OutboxMessage>(domainEvents.Count);
        foreach (var domainEvent in domainEvents)
        {
            var notification = notificationFactory.Create(domainEvent);
            if (notification is null) continue;

            // messageId / occurredOnUtc sourced from IDomainEvent intrinsic properties;
            // transit metadata (TenantId / CorrelationId / CausationId) from IExecutionContext.
            outboxMessages.Add(outboxFactory.Create(
                notification,
                domainEvent.EventId,    // IDomainEvent.EventId (Guid) — stable end-to-end id
                domainEvent.OccurredAt, // IDomainEvent.OccurredAt (DateTimeOffset)
                executionContext));
        }

        if (outboxMessages.Count == 0) return;

        // P4 — single batch write to the transactional outbox (one DB round-trip).
        // Rows are staged in the EF Core change tracker; caller's SaveChanges commits them
        // atomically with domain aggregate changes in the same database transaction.
        await outboxWriter.AddBatchAsync(outboxMessages, ct).ConfigureAwait(false);
    }
}
