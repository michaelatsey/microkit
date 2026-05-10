using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Cqrs.Abstractions.Events;

public interface IDomainEventProvider
{
    IReadOnlyCollection<IDomainEvent>? GetAllDomainEvents();
    void ClearAllDomainEvents();
}
