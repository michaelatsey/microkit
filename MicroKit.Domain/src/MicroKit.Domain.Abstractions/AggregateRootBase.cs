using MicroKit.Domain.Contracts;
using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Abstractions;

/// <summary>
/// Base implementation for aggregate roots without a typed key. Provides domain event management,
/// optimistic concurrency versioning, and row-version support. Extend
/// <see cref="AggregateRootBase{TKey}"/> when a typed identity is needed.
/// </summary>
[Serializable]
public abstract class AggregateRootBase
    : Entity,
    IAggregateRoot,
    IHasDomainEvents
{
    /// <summary>Gets the current version of this aggregate, incremented on each state change.</summary>
    public int Version { get; private set; } = 0;

    /// <summary>Gets the optimistic concurrency row-version token, set by the persistence layer.</summary>
    public byte[]? RowVersion { get; private set; }

    private List<IDomainEvent> _domainEvents = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Appends a domain event to the pending events collection. Call from domain methods that change state.</summary>
    protected void AddDomainEvent(IDomainEvent @event) => _domainEvents.Add(@event);

    /// <summary>Removes a specific domain event raised by this aggregate. Use only within the aggregate.</summary>
    protected void RemoveDomainEvent(IDomainEvent @event) => _domainEvents.Remove(@event);

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Increments the aggregate version. Call inside domain methods that change state.</summary>
    protected void IncrementVersion() => Version++;
}

/// <summary>
/// Base implementation for aggregate roots with a strongly-typed identity key. Extends
/// <see cref="AggregateRootBase"/> so that domain event management and versioning logic
/// live in exactly one place.
/// </summary>
/// <typeparam name="TKey">The type of the aggregate identity key.</typeparam>
[Serializable]
public abstract class AggregateRootBase<TKey>
    : AggregateRootBase,
    IAggregateRoot<TKey>
    where TKey : notnull
{
    /// <summary>Gets the aggregate identity key.</summary>
    public virtual TKey Id { get; private set; }

    /// <summary>
    /// Initializes a new instance without setting the key (for ORM deserialization).
    /// </summary>
    protected AggregateRootBase()
    {
        Id = default!;
    }

    /// <summary>
    /// Initializes a new instance with the specified identity key.
    /// </summary>
    /// <param name="id">The aggregate identity key. Must not be the default value for the type.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is the default value.</exception>
    protected AggregateRootBase(TKey id)
    {
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
            throw new ArgumentException("Entity Id cannot be the default value.", nameof(id));
        Id = id;
    }

    /// <inheritdoc/>
    public override object[] GetKeys() => [Id];
}
