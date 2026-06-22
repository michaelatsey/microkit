namespace MicroKit.MediatR.Events;

/// <summary>
/// A MediatR notification that wraps a domain event of type <typeparamref name="TEvent"/>.
/// Derive from <see cref="DomainEventNotification{TEvent}"/> to obtain a concrete notification class.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
public interface IDomainEventNotification<out TEvent> : INotification
    where TEvent : MicroKit.Domain.Events.IDomainEvent
{
    /// <summary>The domain event that triggered this notification.</summary>
    TEvent DomainEvent { get; }
}
