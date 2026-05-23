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
    public TId Id { get; protected init; }

    protected Entity(TId id)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other || GetType() != other.GetType())
            return false;

        return Id.Equals(other.Id);
    }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null || GetType() != other.GetType())
            return false;

        return Id.Equals(other.Id);
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }
}