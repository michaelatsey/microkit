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
/// <strong>Cascade scenario:</strong> a notification handler (post-commit outbox path) may
/// modify aggregates or call domain services that accumulate new domain events. Those new
/// events are dispatched by <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> after all
/// handlers complete: P2 handlers run and new outbox rows are staged on the scope's
/// <c>DbContext</c>.
/// </para>
/// <para>
/// <strong>Known defect — staged is not saved.</strong> Nothing on the outbox processing path
/// calls <c>SaveChanges</c> after this dispatch, so unless a notification handler happens to
/// commit that same unit of work afterwards, the cascade rows are discarded when the
/// per-message scope is disposed. <b>Do not rely on cascade dispatch from a notification
/// handler.</b> Recorded in the module README under "Still moving"; the fix is a code change and
/// is not made here.
/// </para>
/// <para>
/// <strong>Empty queue:</strong> when no domain events were accumulated,
/// <see cref="IDomainEventsDispatcher.DispatchEventsAsync"/> drains an empty collection and
/// returns without invoking a handler or a sink.
/// </para>
/// <para>
/// Replaces the default <c>ForeachAwaitPublisher</c> when <c>AddMediatRDomainEvents()</c>
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
