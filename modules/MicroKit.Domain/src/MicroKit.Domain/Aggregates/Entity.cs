using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for domain entities with identity-based equality.
/// Entities are defined by their identity, not their attributes.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : IEntityId
{
    /// <summary>
    /// Gets the unique identifier of this entity.
    /// </summary>
    public TId Id { get; protected init; }

    /// <summary>
    /// Initializes a new instance of the entity with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier for this entity</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null</exception>
    protected Entity(TId id)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));
        Id = id;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current entity.
    /// Entities are equal if they have the same type and the same identifier.
    /// </summary>
    /// <param name="obj">The object to compare with the current entity</param>
    /// <returns>True if the specified object is equal to the current entity; otherwise, false</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other || GetType() != other.GetType())
            return false;

        return Id.Equals(other.Id);
    }

    /// <summary>
    /// Determines whether the specified entity is equal to the current entity.
    /// Entities are equal if they have the same type and the same identifier.
    /// </summary>
    /// <param name="other">The entity to compare with the current entity</param>
    /// <returns>True if the specified entity is equal to the current entity; otherwise, false</returns>
    public bool Equals(Entity<TId>? other)
    {
        if (other is null || GetType() != other.GetType())
            return false;

        return Id.Equals(other.Id);
    }

    /// <summary>
    /// Returns a hash code for this entity based on its identifier.
    /// </summary>
    /// <returns>A hash code for the current entity</returns>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// Determines whether two entity instances are equal.
    /// </summary>
    /// <param name="left">The first entity to compare</param>
    /// <param name="right">The second entity to compare</param>
    /// <returns>True if the entities are equal; otherwise, false</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two entity instances are not equal.
    /// </summary>
    /// <param name="left">The first entity to compare</param>
    /// <param name="right">The second entity to compare</param>
    /// <returns>True if the entities are not equal; otherwise, false</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }
}