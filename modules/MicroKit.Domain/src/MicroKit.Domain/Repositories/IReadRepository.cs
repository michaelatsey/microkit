using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.Specifications;

namespace MicroKit.Domain.Repositories;

/// <summary>
/// Abstraction for read-only repository operations.
/// Provides query capabilities without modification concerns.
/// </summary>
/// <typeparam name="TEntity">The aggregate root type</typeparam>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
public interface IReadRepository<TEntity, in TId>
    where TEntity : AggregateRoot<TId>
    where TId : IEntityId
{
    /// <summary>
    /// Finds an entity by its identifier.
    /// Returns null if not found rather than throwing exceptions.
    /// </summary>
    TEntity? FindById(TId id);

    /// <summary>
    /// Gets an entity by its identifier.
    /// Throws EntityNotFoundException if not found.
    /// </summary>
    /// <exception cref="EntityNotFoundException{TEntity, TId}">Entity not found</exception>
    TEntity GetById(TId id);

    /// <summary>
    /// Finds entities matching the given specification.
    /// Returns empty collection if no matches found.
    /// </summary>
    IEnumerable<TEntity> Find(ISpecification<TEntity> specification);

    /// <summary>
    /// Checks if any entities match the given specification.
    /// More efficient than Find().Any() for existence checks.
    /// </summary>
    bool Exists(ISpecification<TEntity> specification);

    /// <summary>
    /// Counts entities matching the given specification.
    /// More efficient than Find().Count() for counting operations.
    /// </summary>
    long Count(ISpecification<TEntity> specification);
}