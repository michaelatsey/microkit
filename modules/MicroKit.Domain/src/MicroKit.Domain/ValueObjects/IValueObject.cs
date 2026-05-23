namespace MicroKit.Domain.ValueObjects;

/// <summary>
/// Marker interface for value objects.
/// Used for generic constraints when working with collections or repositories of value objects.
/// </summary>
/// <remarks>
/// This interface contains no members - it's purely for type constraints.
/// All value objects should be implemented as sealed records or readonly record structs.
/// </remarks>
public interface IValueObject
{
    // Marker interface - no members
}