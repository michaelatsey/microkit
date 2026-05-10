namespace MicroKit.Cqrs.Abstractions.Notifications;

public interface INotificationEventHandler<in TNotifaction> 
    where TNotifaction : INotificationEvent
{
    Task Handle(TNotifaction notification, CancellationToken cancellationToken = default);
}
