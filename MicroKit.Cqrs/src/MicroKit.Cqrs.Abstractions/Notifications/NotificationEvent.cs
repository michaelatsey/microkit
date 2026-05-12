using System.Text.Json.Serialization;
using MicroKit.Cqrs.Abstractions.Events;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Notifications;

/// <summary>Default implementation of <see cref="INotificationEvent{TDomainEvent}"/>.</summary>
/// <typeparam name="TDomainEvent">The domain event type carried by this notification.</typeparam>
public class NotificationEvent<TDomainEvent> : INotificationEvent<TDomainEvent>
        where TDomainEvent : IDomainEvent
{
    /// <inheritdoc/>
    public Guid Id { get; }

    /// <inheritdoc/>
    public TDomainEvent DomainEvent { get; }

    [JsonConstructor]
    protected NotificationEvent(Guid id, TDomainEvent domainEvent)
    {
        Id = id;
        DomainEvent = domainEvent;
    }

    /// <summary>
    /// Creates a new <see cref="NotificationEvent{TDomainEvent}"/> wrapping the specified domain event
    /// with a freshly generated identifier.
    /// </summary>
    /// <param name="domainEvent">The domain event to wrap.</param>
    public static NotificationEvent<TDomainEvent> Create(TDomainEvent domainEvent)
        => new(Guid.NewGuid(), domainEvent);
}
