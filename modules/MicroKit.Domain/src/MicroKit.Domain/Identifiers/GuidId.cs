namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Direct Guid-based identifier for simple cases.
/// Prefer specific record structs like OrderId(Guid) for better type safety.
/// </summary>
public readonly record struct GuidId(Guid Value) : IEntityId
{
    /// <summary>
    /// Creates a new identifier with a time-ordered (UUID version 7) value.
    /// </summary>
    /// <returns>A new GuidId with a unique value</returns>
    /// <remarks>
    /// A version 7 value from <see cref="Guid.CreateVersion7()"/> keeps a primary-key index
    /// compact where the database compares the value as a byte sequence, as PostgreSQL does for
    /// <c>uuid</c>. SQL Server orders <c>uniqueidentifier</c> by a different byte order, so the
    /// value is not sequential there, though no worse than a random version 4 one. EF Core's
    /// <c>SequentialGuidValueGenerator</c> does not run for a key the domain assigned.
    /// </remarks>
    public static GuidId New() => new(Guid.CreateVersion7());

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