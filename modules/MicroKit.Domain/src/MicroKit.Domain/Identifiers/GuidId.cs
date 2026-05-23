namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Direct Guid-based identifier for simple cases.
/// Prefer specific record structs like OrderId(Guid) for better type safety.
/// </summary>
public readonly record struct GuidId(Guid Value) : IEntityId
{
    object IEntityId.Value => Value;

    /// <summary>
    /// Creates a new identifier with a randomly generated Guid.
    /// </summary>
    /// <returns>A new GuidId with a unique value</returns>
    public static GuidId New() => new(Guid.NewGuid());

    /// <summary>
    /// Gets an empty identifier.
    /// </summary>
    public static GuidId Empty => new(Guid.Empty);

    /// <summary>
    /// Creates a new identifier from the specified Guid value.
    /// </summary>
    /// <param name="value">The Guid value to use</param>
    /// <returns>A new GuidId with the specified value</returns>
    public static GuidId From(Guid value) => new(value);

    /// <summary>
    /// Returns a string representation of this identifier.
    /// </summary>
    /// <returns>The string representation of the underlying Guid value</returns>
    public override string ToString() => Value.ToString();
}