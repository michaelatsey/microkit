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
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> equals <c>default(TId)</c>, which is how an uninitialised
    /// <see langword="struct"/> identifier presents itself.
    /// </exception>
    /// <remarks>
    /// There is deliberately no parameterless constructor: one would let an entity exist without an
    /// identity. EF Core does not need it. When it materialises an entity, EF Core calls a
    /// parameterised constructor whose parameter names and types match mapped properties, a
    /// camelCase parameter binding a PascalCase property, so a derived constructor taking
    /// <c>id</c> binds <see cref="Id"/>. The constructor may be private, or protected when
    /// lazy-loading proxies derive from the entity.
    /// </remarks>
    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (EqualityComparer<TId>.Default.Equals(id, default!))
            throw new ArgumentException(
                "Entity identifier cannot be the default value.", nameof(id));
        Id = id;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current entity.
    /// Entities are equal if they have the same identifier and one runtime type is assignable
    /// from the other.
    /// </summary>
    /// <param name="obj">The object to compare with the current entity</param>
    /// <returns>True if the specified object is equal to the current entity; otherwise, false</returns>
    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    /// <summary>
    /// Determines whether the specified entity is equal to the current entity.
    /// Entities are equal if they have the same identifier and one runtime type is assignable
    /// from the other.
    /// </summary>
    /// <param name="other">The entity to compare with the current entity</param>
    /// <returns>True if the specified entity is equal to the current entity; otherwise, false</returns>
    /// <remarks>
    /// <para>
    /// The runtime types are compared by assignability, not exact equality, because an EF Core
    /// lazy-loading or change-tracking proxy is a runtime subclass of the mapped entity: with
    /// exact type equality, an entity and its proxy with the same identifier would compare
    /// unequal. Two unrelated entity types sharing an identifier type stay unequal.
    /// </para>
    /// <para>
    /// The relation is therefore not transitive across sibling subclasses: an <c>A</c> equals a
    /// <c>B : A</c> and a <c>C : A</c> with the same identifier, while the <c>B</c> and the
    /// <c>C</c> are unequal.
    /// </para>
    /// </remarks>
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        var thisType = GetType();
        var otherType = other.GetType();

        if (!thisType.IsAssignableFrom(otherType) && !otherType.IsAssignableFrom(thisType))
            return false;

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
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