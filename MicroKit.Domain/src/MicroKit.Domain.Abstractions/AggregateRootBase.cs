using MicroKit.Domain.Contracts;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Abstractions;

[Serializable]
public abstract class AggregateRootBase 
    : Entity, 
    IAggregateRoot, 
    IHasDomainEvents
{
    public int Version { get; private set; } = 0;

    public byte[]? RowVersion { get; private set; }

    private List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();


    protected void AddDomainEvent(IDomainEvent @event)
    {
        _domainEvents ??= [];
        _domainEvents.Add(@event);
    }

    public void RemoveDomainEvent(IDomainEvent @event)
    {
        _domainEvents?.Remove(@event);
    }

    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }

    public void IncrementVersion() => Version++;

}

[Serializable]
public abstract class AggregateRootBase<TKey> 
    : Entity<TKey>,
    IAggregateRoot<TKey>,
    IHasDomainEvents
    where TKey : notnull
{
    public int Version { get; private set; } = 0;

    public byte[]? RowVersion { get; private set; }

    private List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRootBase()
    {
    }

    protected AggregateRootBase(TKey id)
        : base(id)
    {
    }

    protected void AddDomainEvent(IDomainEvent @event)
    {
        _domainEvents ??= [];
        _domainEvents.Add(@event);
    }

    /// <summary>
    /// Removes the domain event.
    /// </summary>
    /// <param name="event">The event.</param>
    public void RemoveDomainEvent(IDomainEvent @event)
    {
        _domainEvents?.Remove(@event);
    }

    /// <summary>
    /// Clears the domain events.
    /// </summary>
    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }
    public void IncrementVersion() => Version++;
}
