using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Repositories;

/// <summary>
/// Abstraction for full repository operations including mutations.
/// Extends read repository with add, update, and remove operations.
/// </summary>
/// <typeparam name="TEntity">The aggregate root type</typeparam>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
public interface IRepository<TEntity, in TId> : IReadRepository<TEntity, TId>
    where TEntity : AggregateRoot<TId>
    where TId : IEntityId
{
    /// <summary>
    /// Adds a new entity to the repository.
    /// Entity must not already exist (checked by implementation).
    /// </summary>
    void Add(TEntity entity);

    /// <summary>
    /// Updates an existing entity in the repository.
    /// Entity must already exist (checked by implementation).
    /// </summary>
    void Update(TEntity entity);

    /// <summary>
    /// Removes an entity from the repository.
    /// No-op if entity doesn't exist.
    /// </summary>
    void Remove(TEntity entity);

    /// <summary>
    /// Removes an entity by its identifier.
    /// More efficient than loading entity first.
    /// No-op if entity doesn't exist.
    /// </summary>
    void RemoveById(TId id);
}