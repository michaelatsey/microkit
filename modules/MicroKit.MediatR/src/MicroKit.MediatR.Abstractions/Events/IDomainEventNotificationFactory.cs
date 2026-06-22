namespace MicroKit.MediatR.Events;

/// <summary>
/// Resolves the <see cref="IDomainEventNotification{TEvent}"/> associated with a domain event.
/// </summary>
/// <remarks>
/// Returns <see langword="null"/> when no notification wrapper is registered for the event type.
/// The caller may still dispatch the raw domain event through
/// <see cref="Handlers.IDomainEventHandler{TEvent}"/>; notification fan-out is optional.
/// </remarks>
public interface IDomainEventNotificationFactory
{
    /// <summary>
    /// Creates the notification wrapper for <paramref name="domainEvent"/>, or
    /// <see langword="null"/> when this domain event is not mapped to a MediatR notification.
    /// </summary>
    /// <param name="domainEvent">The raw domain event.</param>
    /// <returns>The notification wrapper, or <see langword="null"/>.</returns>
    IDomainEventNotification<MicroKit.Domain.Events.IDomainEvent>? Create(
        MicroKit.Domain.Events.IDomainEvent domainEvent);
}
