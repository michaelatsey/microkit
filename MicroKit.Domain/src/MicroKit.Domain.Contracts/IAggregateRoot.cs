using MicroKit.Domain.Contracts.Events;

namespace MicroKit.Domain.Contracts;

/// <summary>Marks an aggregate root. Extends <see cref="IHasDomainEvents"/> so dispatchers can treat any aggregate uniformly.</summary>
/// <seealso cref="IEntity" />
/// <seealso cref="IHasDomainEvents" />
public interface IAggregateRoot : IEntity, IHasDomainEvents
{
}

/// <summary>Marks an aggregate root with a strongly-typed identity key.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <seealso cref="IEntity{TKey}" />
/// <seealso cref="IAggregateRoot" />
public interface IAggregateRoot<out TKey> : IEntity<TKey>, IAggregateRoot
    where TKey : notnull
{
}