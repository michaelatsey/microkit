using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Contracts;

/// <summary>
/// Contract for aggregate roots. Extends <see cref="IHasDomainEvents"/> so dispatchers can treat any
/// aggregate uniformly, and exposes optimistic-concurrency primitives so persistence layers can enforce
/// conflict detection without casting to a concrete type.
/// </summary>
/// <seealso cref="IEntity" />
/// <seealso cref="IHasDomainEvents" />
public interface IAggregateRoot : IEntity, IHasDomainEvents
{
    /// <summary>Gets the current version of this aggregate, incremented on each state change.</summary>
    int Version { get; }

    /// <summary>Gets the optimistic concurrency row-version token, set by the persistence layer.</summary>
    byte[]? RowVersion { get; }
}

/// <summary>Marks an aggregate root with a strongly-typed identity key.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <seealso cref="IEntity{TKey}" />
/// <seealso cref="IAggregateRoot" />
public interface IAggregateRoot<out TKey> : IEntity<TKey>, IAggregateRoot
    where TKey : notnull
{
}