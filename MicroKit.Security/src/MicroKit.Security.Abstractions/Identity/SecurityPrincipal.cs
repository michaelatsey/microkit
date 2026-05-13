namespace MicroKit.Security.Abstractions.Identity;

/// <summary>
/// Standard implementation of an authenticated security principal.
/// Immutable record optimised for performance and AOT compatibility.
/// </summary>
/// <param name="Identifier">Unique identifier of the principal.</param>
/// <param name="DisplayName">Display name of the principal.</param>
/// <param name="TenantId">Tenant identifier of the principal.</param>
/// <param name="Claims">Claims associated with the principal.</param>
public sealed record SecurityPrincipal(
    string? Identifier,
    string? DisplayName,
    string? TenantId,
    IReadOnlyList<SecurityClaim> Claims) : ISecurityPrincipal
{
    /// <inheritdoc />
    public bool IsAuthenticated => !string.IsNullOrEmpty(Identifier);

    /// <inheritdoc />
    string? ISecurityPrincipal.Identifier => Identifier;

    /// <inheritdoc />
    string? ISecurityPrincipal.TenantId => TenantId;

    /// <inheritdoc />
    public bool HasClaim(string type)
    {
        foreach (var claim in Claims)
        {
            if (claim.IsType(type))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool HasClaim(string type, string value)
    {
        foreach (var claim in Claims)
        {
            if (claim.Matches(type, value))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public string? GetClaimValue(string type)
    {
        foreach (var claim in Claims)
        {
            if (claim.IsType(type))
                return claim.Value;
        }
        return null;
    }

    /// <summary>
    /// Returns a new principal with additional claims appended.
    /// </summary>
    /// <param name="additionalClaims">Claims to add.</param>
    /// <returns>New principal with the combined claims.</returns>
    public SecurityPrincipal WithClaims(params SecurityClaim[] additionalClaims)
    {
        if (additionalClaims.Length == 0) return this;

        var combinedClaims = new List<SecurityClaim>(Claims.Count + additionalClaims.Length);
        combinedClaims.AddRange(Claims);
        combinedClaims.AddRange(additionalClaims);

        return this with { Claims = combinedClaims };
    }

    /// <summary>
    /// Returns a new principal associated with the specified tenant.
    /// Only use this when intentionally switching tenant context (e.g. admin impersonation).
    /// </summary>
    public SecurityPrincipal WithTenant(string? identityTenantId)
    {
        return this with { TenantId = identityTenantId };
    }
}
