namespace MicroKit.Events.Contracts;

/// <summary>Publishes domain events to registered handlers.</summary>
public interface IEventPublisher
{
    /// <summary>Publishes a single event to all registered handlers.</summary>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Publishes a collection of events to their registered handlers.</summary>
    /// <param name="events">The events to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task PublishRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default);
}
