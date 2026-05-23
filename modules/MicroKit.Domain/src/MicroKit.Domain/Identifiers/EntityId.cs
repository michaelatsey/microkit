namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Base implementation for strongly-typed entity identifiers using Guid values.
/// Provides common functionality like equality, hashing, and string representation.
/// Inherit from this to create specific ID types like OrderId : EntityId&lt;OrderId&gt;.
/// </summary>
/// <typeparam name="T">The concrete identifier type for type safety</typeparam>
public abstract record EntityId<T> : IEntityId where T : EntityId<T>
{
    /// <summary>
    /// Gets the underlying Guid value of this identifier.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Initializes a new instance of the entity identifier.
    /// </summary>
    /// <param name="value">The Guid value for this identifier</param>
    /// <exception cref="ArgumentException">Thrown when the value is <see cref="Guid.Empty"/></exception>
    protected EntityId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Entity identifier cannot be empty.", nameof(value));
        Value = value;
    }

    object IEntityId.Value => Value;

    /// <summary>
    /// Returns a string representation of this identifier.
    /// </summary>
    /// <returns>The string representation of the underlying Guid value</returns>
    public override string ToString() => Value.ToString();
}