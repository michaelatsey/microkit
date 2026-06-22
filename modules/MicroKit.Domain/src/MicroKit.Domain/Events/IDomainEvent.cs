namespace MicroKit.Domain.Events;

/// <summary>
/// Represents something significant that happened in the domain.
/// Events are immutable facts about the past.
/// </summary>
public interface IDomainEvent : IEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// When this event occurred in UTC.
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
