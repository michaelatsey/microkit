namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Marker interface for all strongly-typed entity identifiers.
/// Ensures type safety and prevents primitive obsession in domain models.
/// </summary>
public interface IEntityId
{
    /// <summary>
    /// Gets the underlying primitive value of this identifier.
    /// </summary>
    object Value { get; }
}