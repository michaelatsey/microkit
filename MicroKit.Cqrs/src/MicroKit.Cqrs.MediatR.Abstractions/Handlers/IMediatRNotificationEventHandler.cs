using MediatR;
using MicroKit.Cqrs.Abstractions.Notifications;

namespace MicroKit.Cqrs.MediatR.Abstractions.Handlers;

public interface IMediatRNotificationEventHandler<TNotification> 
    : INotificationEventHandler<TNotification>, INotificationHandler<TNotification>
    where TNotification : INotificationEvent, INotification
{
}
