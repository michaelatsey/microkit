using MicroKit.Cqrs.Abstractions.Events;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Notifications;

public interface INotificationEvent
{
    Guid Id { get; }
}


/// <summary>A notification event that wraps a specific domain event type.</summary>
/// <typeparam name="TEventType">The domain event type carried by this notification.</typeparam>
public interface INotificationEvent<out TEventType> : INotificationEvent
    where TEventType : IDomainEvent
{
    /// <summary>
    /// Gets the domain event.
    /// </summary>
    /// <value>
    /// The domain event.
    /// </value>
    TEventType DomainEvent { get; }
}
