namespace MicroKit.Domain.Events;

/// <summary>
/// Abstract base for domain events with automatic metadata.
/// Inherit from this and use sealed records for concrete events.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}