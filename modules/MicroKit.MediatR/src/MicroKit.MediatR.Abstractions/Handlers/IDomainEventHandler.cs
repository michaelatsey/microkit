namespace MicroKit.MediatR;

/// <summary>
/// Handles a MediatR notification that wraps a <typeparamref name="TEvent"/> domain event.
/// Returns <see cref="Task"/> (not <see cref="ValueTask"/>) to satisfy the MediatR
/// <c>INotificationHandler</c> contract.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
/// <typeparam name="TNotification">The MediatR notification type wrapping the event.</typeparam>
public interface IDomainEventHandler<TEvent, in TNotification>
    where TEvent : IEvent
    where TNotification : IDomainEventNotification<TEvent>
{
    /// <summary>Handles the domain event notification.</summary>
    /// <param name="notification">The notification containing the domain event.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous notification handling.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
