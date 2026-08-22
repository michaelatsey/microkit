using MicroKit.Messaging.Outbox;

namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// The <see cref="IDomainEventsSink"/> contributed by <c>MicroKit.Messaging.MediatR</c>. Maps each
/// domain event in the batch to its <see cref="IDomainEventNotification{TEventType}"/> and stages
/// every mapped notification in the transactional outbox with a single batched write.
/// </summary>
/// <remarks>
/// <para>
/// This is P3 + P4 of the dispatch topology. P1 (drain) and P2 (synchronous
/// <see cref="IDomainEventHandler{TEvent}"/> dispatch) belong to the core orchestrator, which runs
/// them for the whole batch before invoking any sink — so a P2 handler can never observe a
/// partially written outbox batch (ADR-MEDIATR-014).
/// </para>
/// <list type="number">
///   <item>P3 — for each domain event, build the notification wrapper via
///         <see cref="IDomainEventNotificationFactory"/>. Events with no registered notification
///         type produce <see langword="null"/> and are skipped.</item>
///   <item>P4 — write every notification to the outbox in one
///         <see cref="IOutboxWriter.AddBatchAsync"/> call (one DB round-trip regardless of event
///         count). <c>MessageId</c>/<c>OccurredOnUtc</c> come from the event's intrinsic
///         properties; transit metadata (<c>TenantId</c>/<c>CorrelationId</c>/<c>CausationId</c>)
///         from <see cref="IExecutionContext"/>, never from the payload (ADR-MSG-008).</item>
/// </list>
/// <para>
/// Rows are staged in the caller's unit of work; the caller's <c>SaveChanges</c> commits them
/// atomically with the domain aggregate changes in the same database transaction. This sink
/// performs no I/O to any external system.
/// </para>
/// <para>
/// <strong>Handler disjointness:</strong> <see cref="IDomainEventHandler{TEvent}"/> (P2) and
/// <see cref="INotificationHandler{TNotification}"/> (outbox relay, post-commit) are structurally
/// disjoint dispatch paths. Because an outbox retry re-publishes the notification and re-runs ALL
/// of its notification handlers, those handlers must be idempotent (ADR-MSG-003 / ADR-MSG-009).
/// </para>
/// </remarks>
internal sealed class OutboxDomainEventSink(
    IDomainEventNotificationFactory notificationFactory,
    OutboxMessageFactory outboxFactory,
    IOutboxWriter outboxWriter,
    IExecutionContext executionContext)
    : IDomainEventsSink
{
    /// <inheritdoc />
    public async ValueTask ReceiveAsync(
        IReadOnlyList<IDomainEvent> domainEvents,
        CancellationToken ct = default)
    {
        // P3 — collect the notification wrappers for events that have one registered.
        // Each domain event maps to at most one notification type (ADR-MEDIATR-005).
        var outboxMessages = new List<OutboxMessage>(domainEvents.Count);
        foreach (var domainEvent in domainEvents)
        {
            var notification = notificationFactory.Create(domainEvent);
            if (notification is null) continue;

            outboxMessages.Add(outboxFactory.Create(
                notification,
                domainEvent.EventId,    // IDomainEvent.EventId (Guid) — stable end-to-end id
                domainEvent.OccurredAt, // IDomainEvent.OccurredAt (DateTimeOffset)
                executionContext));
        }

        if (outboxMessages.Count == 0) return;

        // P4 — single batch write to the transactional outbox (one DB round-trip).
        await outboxWriter.AddBatchAsync(outboxMessages, ct).ConfigureAwait(false);
    }
}
