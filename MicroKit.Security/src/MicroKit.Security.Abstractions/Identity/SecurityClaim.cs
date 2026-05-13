namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Represents an immutable, AOT-compatible security claim.
/// Struct layout avoids heap allocations.
/// </summary>
/// <param name="Type">The claim type (e.g. "role", "sub", "email").</param>
/// <param name="Value">The claim value.</param>
public readonly record struct SecurityClaim(string Type, string Value)
{
    /// <summary>
    /// Empty claim representing the absence of a value.
    /// </summary>
    public static SecurityClaim Empty => new(string.Empty, string.Empty);

    /// <summary>
    /// Indicates whether the claim is empty (undefined type).
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Type);

    /// <summary>
    /// Returns true if the claim type matches the specified type.
    /// </summary>
    /// <param name="type">The type to match.</param>
    /// <returns>True if the type matches, false otherwise.</returns>
    public bool IsType(string type) => string.Equals(Type, type, StringComparison.Ordinal);

    /// <summary>
    /// Returns true if the claim matches both the specified type and value.
    /// </summary>
    /// <param name="type">The type to match.</param>
    /// <param name="value">The value to match.</param>
    /// <returns>True if both type and value match, false otherwise.</returns>
    public bool Matches(string type, string value) =>
        string.Equals(Type, type, StringComparison.Ordinal) &&
        string.Equals(Value, value, StringComparison.Ordinal);
}
