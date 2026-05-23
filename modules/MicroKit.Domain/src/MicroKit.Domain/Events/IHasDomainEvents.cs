namespace MicroKit.Domain.Events;

/// <summary>
/// Marker interface for entities that can raise domain events.
/// Provides read-only access to domain events for external consumption.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>
    /// Gets the domain events that have been raised by this entity.
    /// Events represent facts about what has already happened in the domain.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
}