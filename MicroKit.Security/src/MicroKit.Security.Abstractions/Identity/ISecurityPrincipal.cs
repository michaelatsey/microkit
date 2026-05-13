namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Represents the authenticated identity of a security principal.
/// Primary interface for accessing identity information within MicroKit.
/// </summary>
public interface ISecurityPrincipal
{
    /// <summary>
    /// Unique identifier of the principal (e.g. user ID, client ID).
    /// Null for anonymous principals.
    /// </summary>
    string? Identifier { get; }

    /// <summary>
    /// Display name of the principal.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// Tenant identifier from the JWT the principal belongs to.
    /// Null if the principal is not associated with a specific tenant
    /// or if multi-tenancy is not in use.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Indicates whether the principal is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Claims associated with the principal.
    /// </summary>
    IReadOnlyList<SecurityClaim> Claims { get; }

    /// <summary>
    /// Returns true if the principal has a claim of the specified type.
    /// </summary>
    /// <param name="type">The claim type to search for.</param>
    /// <returns>True if a claim of this type exists, false otherwise.</returns>
    bool HasClaim(string type);

    /// <summary>
    /// Returns true if the principal has a claim matching both the specified type and value.
    /// </summary>
    /// <param name="type">The claim type.</param>
    /// <param name="value">The claim value.</param>
    /// <returns>True if the exact claim exists, false otherwise.</returns>
    bool HasClaim(string type, string value);

    /// <summary>
    /// Returns the value of the first claim matching the specified type.
    /// </summary>
    /// <param name="type">The claim type to search for.</param>
    /// <returns>The claim value, or null if not found.</returns>
    string? GetClaimValue(string type);
}
