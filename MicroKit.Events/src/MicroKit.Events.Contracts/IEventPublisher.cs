namespace MicroKit.Events.Contracts;

public interface IEventPublisher
{
    /// <summary>
    /// Publie un événement
    /// </summary>
    /// <param name="event">L'événement à publier</param>
    /// <param name="cancellationToken">Le jeton d'annulation</param>
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publie plusieurs événements
    /// </summary>
    /// <param name="events">Les événements à publier</param>
    /// <param name="cancellationToken">Le jeton d'annulation</param>
    Task PublishRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default);
}
