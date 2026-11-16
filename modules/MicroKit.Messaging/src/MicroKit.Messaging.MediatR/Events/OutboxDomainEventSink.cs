using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Stores;

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
///   <item>P4 — write the notifications to the outbox of their store, one
///         <see cref="IOutboxWriter.AddBatchAsync"/> call per store. <c>MessageId</c>/<c>OccurredOnUtc</c> come from the event's intrinsic
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
    MessageStoreResolver stores,
    DeliveryStoreHolder deliveryStore,
    IServiceProvider services,
    IExecutionContext executionContext)
    : IDomainEventsSink
{
    /// <inheritdoc />
    public async ValueTask ReceiveAsync(
        IReadOnlyList<IDomainEvent> domainEvents,
        CancellationToken ct = default)
    {
        // P3 — collect the notification wrappers for events that have one registered, each with
        // the store it is enlisted in (#188: the store of the delivery running here, the only
        // store, or the store bound to the notification type's assembly).
        var byStore = new Dictionary<MessageStoreKey, List<OutboxMessage>>();
        foreach (var domainEvent in domainEvents)
        {
            var notification = notificationFactory.Create(domainEvent);
            if (notification is null) continue;

            var store = stores.ForPublication(notification.GetType(), explicitStore: null, deliveryStore.Store)
                ?? throw new OutboxConfigurationException(
                    $"Domain event '{domainEvent.GetType().Name}' maps to notification " +
                    $"'{notification.GetType().Name}', but no message store is configured, so it " +
                    "has no outbox to be staged in. Configure one — " +
                    "bus.UseEntityFrameworkOutbox<TContext>() — next to AddMediatRDomainEvents().");

            if (!byStore.TryGetValue(store, out var rows))
            {
                byStore[store] = rows = [];
            }

            rows.Add(outboxFactory.CreateNotification(
                notification,
                domainEvent.EventId,    // IDomainEvent.EventId (Guid) — stable end-to-end id
                domainEvent.OccurredAt, // IDomainEvent.OccurredAt (DateTimeOffset)
                executionContext));
        }

        // P4 — one batched write per store into the scope's unit of work.
        foreach (var (store, rows) in byStore)
        {
            await services.GetRequiredKeyedService<IOutboxWriter>(store)
                .AddBatchAsync(rows, ct)
                .ConfigureAwait(false);
        }
    }
}
