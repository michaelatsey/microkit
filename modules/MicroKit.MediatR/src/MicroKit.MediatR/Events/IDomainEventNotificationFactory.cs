namespace MicroKit.MediatR.Events;

/// <summary>
/// Internal singleton that maps domain event types to their registered notification factories.
/// Built at DI startup from the <c>IDomainEventHandler</c> scan — no runtime reflection.
/// </summary>
internal interface IDomainEventNotificationFactory
{
    /// <summary>
    /// Creates the <see cref="INotification"/> wrapper for the given <paramref name="domainEvent"/>.
    /// </summary>
    /// <param name="domainEvent">The domain event to wrap.</param>
    /// <returns>The notification ready for MediatR publish.</returns>
    /// <exception cref="InvalidOperationException">No notification registered for this event type.</exception>
    INotification Create(IEvent domainEvent);
}
