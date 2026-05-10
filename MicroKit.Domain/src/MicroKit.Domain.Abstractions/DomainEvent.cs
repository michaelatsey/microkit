using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Abstractions;

/// <summary>
/// Implémentation de base d'un événement de domaine
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredOnUtc { get; private set; }
    protected DomainEvent()
    {
        Id = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }
}
