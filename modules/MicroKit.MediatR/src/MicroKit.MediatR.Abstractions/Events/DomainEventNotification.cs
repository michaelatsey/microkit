namespace MicroKit.MediatR.Events;

/// <summary>
/// Abstract base class for notifications that wrap a domain event of type <typeparamref name="TEvent"/>.
/// Derive from this class to create a concrete MediatR notification for a domain event.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
/// <remarks>
/// Derived classes must call <c>base(domainEvent)</c> from their constructor.
/// </remarks>
/// <example>
/// <code>
/// public sealed class UserRegisteredNotification : DomainEventNotification&lt;UserRegisteredEvent&gt;
/// {
///     public UserRegisteredNotification(UserRegisteredEvent domainEvent) : base(domainEvent) { }
/// }
/// </code>
/// </example>
public abstract class DomainEventNotification<TEvent>(TEvent domainEvent)
    : IDomainEventNotification<TEvent>
    where TEvent : IDomainEvent
{
    /// <inheritdoc />
    public TEvent DomainEvent { get; } = domainEvent;
}
