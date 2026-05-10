
using MediatR;
using MicroKit.Cqrs.Abstractions.Events;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.MediatR.Abstractions.Handlers;

/// <summary>
/// Represents the domain event notification handler.
/// </summary>
/// <typeparam name="TEvent">The type of the event.</typeparam>
/// <seealso cref="INotificationHandler&lt;TEvent&gt;" />
public interface IMediatRDomainEventHandler<TEvent> 
    : IDomainEventHandler<TEvent>, INotificationHandler<TEvent>
    where TEvent : IDomainEvent, INotification
{
}
