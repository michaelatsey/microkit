namespace MicroKit.MediatR;

/// <summary>
/// A MediatR notification that wraps a domain event of type <typeparamref name="TEvent"/>.
/// Derive from <see cref="DomainEventNotification{TEvent}"/> to obtain a concrete notification class.
/// </summary>
/// <typeparam name="TEvent">The domain event type, which must implement <see cref="IEvent"/>.</typeparam>
public interface IDomainEventNotification<out TEvent> : INotification
    where TEvent : IEvent
{
    /// <summary>The domain event that triggered this notification.</summary>
    TEvent DomainEvent { get; }
}
