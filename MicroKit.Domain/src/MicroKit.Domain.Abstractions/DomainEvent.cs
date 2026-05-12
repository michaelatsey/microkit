using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Abstractions;

/// <summary>Base record for domain events — provides a unique identifier and occurrence timestamp.</summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>Initializes a new domain event with a fresh <see cref="Guid"/> and the current UTC time.</summary>
    protected DomainEvent()
    {
        Id = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }
}
