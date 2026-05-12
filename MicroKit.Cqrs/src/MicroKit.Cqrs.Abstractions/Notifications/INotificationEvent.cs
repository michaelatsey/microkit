using MicroKit.Cqrs.Abstractions.Events;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Notifications;

/// <summary>Marker interface for all notification events dispatched through the MediatR pipeline.</summary>
public interface INotificationEvent
{
    /// <summary>Gets the unique identifier of this notification event.</summary>
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
