using MediatR;
using MicroKit.Cqrs.Abstractions.Notifications;

namespace MicroKit.Cqrs.MediatR.Abstractions.Handlers;

/// <summary>Combined MediatR notification handler and MicroKit notification event handler for <typeparamref name="TNotification"/>.</summary>
/// <typeparam name="TNotification">The notification type, which must implement both <see cref="INotificationEvent"/> and <see cref="INotification"/>.</typeparam>
public interface IMediatRNotificationEventHandler<TNotification>
    : INotificationEventHandler<TNotification>, INotificationHandler<TNotification>
    where TNotification : INotificationEvent, INotification
{
}
