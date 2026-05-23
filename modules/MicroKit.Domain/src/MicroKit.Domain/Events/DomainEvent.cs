namespace MicroKit.Domain.Events;

/// <summary>
/// Abstract base for domain events with automatic metadata.
/// Inherit from this and use sealed records for concrete events.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>
    /// Gets the unique identifier for this domain event.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the date and time when this domain event occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}