namespace MicroKit.MediatR;

internal sealed class DomainEventHandlerAdapter<TEvent, TNotification>(
    IDomainEventHandler<TEvent, TNotification> inner)
    : INotificationHandler<TNotification>
    where TEvent : IEvent
    where TNotification : class, IDomainEventNotification<TEvent>
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
        => inner.Handle(notification, cancellationToken);
}
