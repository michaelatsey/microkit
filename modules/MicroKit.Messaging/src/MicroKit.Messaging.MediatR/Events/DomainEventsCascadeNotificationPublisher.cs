namespace MicroKit.Messaging.MediatR.Events;

/// <summary>
/// Custom <see cref="INotificationPublisher"/> that dispatches domain events accumulated
/// during notification handler execution (cascade event support).
/// </summary>
/// <remarks>
/// <para>
/// This is the MediatR fan-out adaptation of the notification-handler decorator pattern.
/// Instead of wrapping each individual <see cref="INotificationHandler{TNotification}"/>,
/// this publisher wraps the execution of ALL handlers for a notification and calls
/// <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> once after all handlers complete.
/// </para>
/// <para>
/// <strong>Cascade scenario:</strong> a notification handler (P4, post-commit outbox path)
/// may modify aggregates or call domain services that accumulate new domain events. Those
/// new events are dispatched by <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/>
/// after all handlers complete — P2 handlers run, new P3/P4 outbox rows are staged — all
/// within the same outbox processor scope, without committing a new transaction.
/// </para>
/// <para>
/// <strong>No-op on empty queue:</strong> if no domain events were accumulated during
/// handler execution, <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> returns
/// immediately with zero overhead.
/// </para>
/// <para>
/// Replaces the default <c>ForeachAwaitPublisher</c> when <c>AddMediatRTransport()</c>
/// is called. Registered as transient so the scoped <see cref="IDomainEventsDispatcher"/>
/// is resolved correctly within each request scope.
/// </para>
/// </remarks>
internal sealed class DomainEventsCascadeNotificationPublisher(
    IDomainEventsDispatcher domainEventsDispatcher)
    : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        foreach (var executor in handlerExecutors)
            await executor.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);

        await domainEventsDispatcher.DispatchEventsAsync(cancellationToken).ConfigureAwait(false);
    }
}
