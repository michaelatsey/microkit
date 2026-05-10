using MediatR;
using MicroKit.Domain.Abstractions;

namespace MicroKit.Cqrs.MediatR.Abstractions;

public class MediatRNotificationWrapper<TEvent> : INotification
    where TEvent : DomainEvent
{
    public TEvent DomainEvent { get; }
    public MediatRNotificationWrapper(TEvent domainEvent) => DomainEvent = domainEvent;
}
