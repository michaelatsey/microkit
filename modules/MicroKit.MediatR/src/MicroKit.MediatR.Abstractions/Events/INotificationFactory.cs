namespace MicroKit.MediatR;

/// <summary>
/// Resolves the <see cref="IDomainEventNotification{TEvent}"/> associated with a domain event.
/// </summary>
/// <remarks>
/// Used by <c>DomainEventsDispatcher</c> to wrap each accumulated domain event in its
/// registered notification type before adding it to the outbox. Returns <see langword="null"/>
/// when no notification is registered for the given event type (event is dispatch-only,
/// no outbox persistence required).
/// </remarks>
public interface INotificationFactory
{
    /// <summary>
    /// Creates the <see cref="IDomainEventNotification{TEvent}"/> for <paramref name="domainEvent"/>,
    /// or <see langword="null"/> if no notification type is registered for this event type.
    /// </summary>
    /// <param name="domainEvent">The domain event to wrap.</param>
    /// <returns>
    /// The notification ready for outbox persistence, or <see langword="null"/> if this event
    /// has no registered notification type.
    /// </returns>
    IDomainEventNotification<IEvent>? Create(IEvent domainEvent);
}
