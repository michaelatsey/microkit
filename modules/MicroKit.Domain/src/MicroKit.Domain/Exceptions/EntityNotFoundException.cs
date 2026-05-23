using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Exceptions;

/// <summary>
/// Thrown when an entity with a specific identifier cannot be found.
/// Strongly-typed to preserve ID type information.
/// </summary>
/// <typeparam name="TEntity">The entity type that was not found</typeparam>
/// <typeparam name="TId">The identifier type used in the search</typeparam>
public sealed class EntityNotFoundException<TEntity, TId> : DomainException
    where TId : IEntityId
{
    public TId EntityId { get; }
    public Type EntityType { get; }

    public EntityNotFoundException(TId entityId)
        : base($"{typeof(TEntity).Name} with ID '{entityId}' was not found.")
    {
        EntityId = entityId;
        EntityType = typeof(TEntity);
    }
}