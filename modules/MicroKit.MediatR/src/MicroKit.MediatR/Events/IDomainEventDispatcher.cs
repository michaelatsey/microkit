namespace MicroKit.MediatR;

/// <summary>
/// Publishes domain events from command handlers without coupling them to <c>IMediator</c>.
/// Inject this interface into command handlers — never inject <c>IMediator</c> directly.
/// </summary>
/// <remarks>
/// Publish events <b>after</b> persistence — the event is a fait accompli.
/// The dispatcher wraps the event in its registered notification type and dispatches
/// it via MediatR's publish pipeline. Notification mapping is built at DI startup.
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Wraps <paramref name="domainEvent"/> in its registered <see cref="DomainEventNotification{TEvent}"/>
    /// subclass and publishes it to all registered handlers.
    /// </summary>
    /// <param name="domainEvent">The domain event that occurred. Must have a registered notification type.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="DomainEventNotification{TEvent}"/> is registered for the event type.
    /// </exception>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous publish operation.</returns>
    ValueTask PublishAsync(IEvent domainEvent, CancellationToken ct = default);
}
