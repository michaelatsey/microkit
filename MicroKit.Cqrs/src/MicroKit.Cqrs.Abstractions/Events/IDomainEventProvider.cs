using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Events;

/// <summary>Exposes the domain events accumulated by an aggregate or entity for collection by the dispatcher.</summary>
public interface IDomainEventProvider
{
    /// <summary>Returns all domain events raised since the last clear, or <see langword="null"/> if none.</summary>
    IReadOnlyCollection<IDomainEvent>? GetAllDomainEvents();

    /// <summary>Removes all accumulated domain events, typically called after successful dispatch.</summary>
    void ClearAllDomainEvents();
}
