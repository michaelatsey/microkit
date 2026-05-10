using MicroKit.Cqrs.Abstractions.Events;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Notifications;

public interface INotificationEvent
{
    Guid Id { get; }
}


/// <summary>
/// 
/// </summary>
/// <typeparam name="TEventType">The type of the event type.</typeparam>
/// <seealso cref="INotification" />
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
