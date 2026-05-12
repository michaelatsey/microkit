namespace MicroKit.Cqrs.Abstractions.Notifications;

/// <summary>Handles a notification event of type <typeparamref name="TNotifaction"/>.</summary>
/// <typeparam name="TNotifaction">The notification event type to handle.</typeparam>
public interface INotificationEventHandler<in TNotifaction>
    where TNotifaction : INotificationEvent
{
    /// <summary>Processes the notification event.</summary>
    /// <param name="notification">The notification to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task Handle(TNotifaction notification, CancellationToken cancellationToken = default);
}
