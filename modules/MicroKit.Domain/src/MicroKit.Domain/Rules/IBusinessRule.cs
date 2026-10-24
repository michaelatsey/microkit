namespace MicroKit.Domain.Rules;

/// <summary>
/// Represents a domain business rule that can be validated.
/// Should be stateless and deterministic.
/// </summary>
public interface IBusinessRule
{
    /// <summary>
    /// Checks if this business rule is currently violated.
    /// Should be side-effect free and deterministic.
    /// </summary>
    /// <returns>True if the rule is broken, false otherwise</returns>
    bool IsBroken();

    /// <summary>
    /// Gets a human-readable message describing what this rule enforces.
    /// Used in exception messages when the rule is violated.
    /// </summary>
    string Message { get; }

    /// <summary>
    /// Gets the stable, machine-readable code that identifies this rule, such as
    /// <c>order.must-have-items</c>.
    /// </summary>
    /// <remarks>
    /// The code is a contract: consumers persist it, translate it and map it to API error codes.
    /// Keep it stable across renames of the implementing type, which is why it is never derived
    /// from the type name.
    /// </remarks>
    string Code { get; }
}