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
}