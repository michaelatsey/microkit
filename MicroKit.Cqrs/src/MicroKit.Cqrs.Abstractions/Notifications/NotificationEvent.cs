using System.Text.Json.Serialization;
using MicroKit.Cqrs.Abstractions.Events;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Notifications;

public class NotificationEvent<TDomainEvent> : INotificationEvent<TDomainEvent>
        where TDomainEvent : IDomainEvent
{
    public Guid Id { get; }
    public TDomainEvent DomainEvent { get; }
    [JsonConstructor]
    protected NotificationEvent(Guid id, TDomainEvent domainEvent)
    {
        Id = id;
        DomainEvent = domainEvent;
    }
}
