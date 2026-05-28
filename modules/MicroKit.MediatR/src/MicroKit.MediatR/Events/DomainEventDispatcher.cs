namespace MicroKit.MediatR;

internal sealed class DomainEventDispatcher(
    IPublisher publisher,
    IDomainEventNotificationFactory factory) : IDomainEventDispatcher
{
    public ValueTask PublishAsync(IEvent domainEvent, CancellationToken ct = default)
    {
        var notification = factory.Create(domainEvent);
        // Wrap Task directly — avoids async state machine allocation on every domain event dispatch.
        return new ValueTask(publisher.Publish(notification, ct));
    }
}
