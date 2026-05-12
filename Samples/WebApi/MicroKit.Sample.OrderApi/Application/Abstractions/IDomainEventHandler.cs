using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

/// <summary>MediatR notification handler interface for domain events.</summary>
public interface IDomainEventHandler<in TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
}
