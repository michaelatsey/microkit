using MediatR;

namespace MicroKit.Sample.OrderApi.Application.Abstractions;

// Le Handler : le traitement à exécuter suite à l'événement
public interface IDomainEventHandler<in TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
}
