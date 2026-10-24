using MicroKit.Domain.Events;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.Rules;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for aggregate roots in DDD.
/// Manages domain events and serves as consistency boundary.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IDomainEventsProvider, IAggregateRoot
    where TId : IEntityId
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this aggregate root.</param>
    protected AggregateRoot(TId id) : base(id) { }

    /// <summary>
    /// Gets all domain events raised by this aggregate.
    /// Events are read-only to prevent external modification.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents =>
        _domainEvents.Count == 0 ? Array.Empty<IDomainEvent>() : _domainEvents.AsReadOnly();

    /// <summary>
    /// Raises a domain event. Call AFTER state mutations — events are facts about what has
    /// already happened.
    /// </summary>
    /// <param name="domainEvent">The event that occurred.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="domainEvent"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The event is stored exactly as supplied: the aggregate never rewrites a fact it was
    /// handed. Timestamping is the caller's responsibility, and the compiler enforces it: an event
    /// deriving from <see cref="DomainEvent"/> passes its instant through the positional
    /// <c>OccurredAt</c> parameter of the base record, which has no default. Pass the same instant
    /// to every event raised within one business operation.
    /// </remarks>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Atomically retrieves all domain events and clears the internal collection.
    /// This method implements the "drain" pattern where events are collected and
    /// then removed in a single operation to prevent duplicate processing.
    /// </summary>
    /// <returns>
    /// A read-only collection containing all domain events that were raised.
    /// Returns an empty collection if no events were raised.
    /// </returns>
    /// <remarks>
    /// This method should be called by the Application/Infrastructure layers
    /// after successfully persisting the aggregate's state changes.
    /// </remarks>
    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        if (_domainEvents.Count == 0)
            return Array.Empty<IDomainEvent>();

        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }

    /// <summary>
    /// Validates a business rule and throws if it's violated.
    /// Centralizes rule checking logic across the aggregate.
    /// </summary>
    /// <param name="rule">The business rule to check</param>
    /// <exception cref="BusinessRuleViolationException">Thrown if rule is broken</exception>
    protected void CheckRule(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsBroken())
            throw new BusinessRuleViolationException(rule);
    }
}