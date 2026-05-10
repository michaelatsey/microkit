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


    protected void AddDomainEvent(IDomainEvent @event) => _domainEvents.Add(@event);

    /// <summary>Removes a specific domain event raised by this aggregate. Use only within the aggregate.</summary>
    protected void RemoveDomainEvent(IDomainEvent @event) => _domainEvents.Remove(@event);

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Increments the aggregate version. Call inside domain methods that change state.</summary>
    protected void IncrementVersion() => Version++;

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

    protected void AddDomainEvent(IDomainEvent @event) => _domainEvents.Add(@event);

    /// <summary>Removes a specific domain event raised by this aggregate. Use only within the aggregate.</summary>
    protected void RemoveDomainEvent(IDomainEvent @event) => _domainEvents.Remove(@event);

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Increments the aggregate version. Call inside domain methods that change state.</summary>
    protected void IncrementVersion() => Version++;
}
