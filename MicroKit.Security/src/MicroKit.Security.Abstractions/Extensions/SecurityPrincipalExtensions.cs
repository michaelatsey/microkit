using MicroKit.Security.Abstractions.Constants;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Extensions;

/// <summary>
/// Extension methods for <see cref="ISecurityPrincipal"/> providing convenient claim accessors.
/// </summary>
public static class SecurityPrincipalExtensions
{
    /// <summary>
    /// Returns the principal's email address.
    /// </summary>
    public static string? GetEmail(this ISecurityPrincipal principal) =>
        principal.GetClaimValue(ClaimTypes.Email);

    /// <summary>
    /// Returns the principal's tenant identifier.
    /// </summary>
    public static string? GetTenantId(this ISecurityPrincipal principal) =>
        principal.GetClaimValue(ClaimTypes.TenantId);

    /// <summary>
    /// Returns the principal's client identifier.
    /// </summary>
    public static string? GetClientId(this ISecurityPrincipal principal) =>
        principal.GetClaimValue(ClaimTypes.ClientId);

    /// <summary>
    /// Returns true if the principal holds the specified role.
    /// </summary>
    public static bool HasRole(this ISecurityPrincipal principal, string role) =>
        principal.HasClaim(ClaimTypes.Role, role) ||
        principal.HasClaim(ClaimTypes.Roles, role);

    /// <summary>
    /// Returns true if the principal holds the specified permission.
    /// </summary>
    public static bool HasPermission(this ISecurityPrincipal principal, string permission) =>
        principal.HasClaim(ClaimTypes.Permissions, permission);

    /// <summary>
    /// Returns all roles held by the principal.
    /// </summary>
    public static IEnumerable<string> GetRoles(this ISecurityPrincipal principal)
    {
        foreach (var claim in principal.Claims)
        {
            if (claim.IsType(ClaimTypes.Role) || claim.IsType(ClaimTypes.Roles))
            {
                yield return claim.Value;
            }
        }
    }

    /// <summary>
    /// Returns all permissions held by the principal.
    /// </summary>
    public static IEnumerable<string> GetPermissions(this ISecurityPrincipal principal)
    {
        foreach (var claim in principal.Claims)
        {
            if (claim.IsType(ClaimTypes.Permissions))
            {
                yield return claim.Value;
            }
        }
    }

    /// <summary>
    /// Returns true if the principal holds at least one of the specified roles.
    /// </summary>
    public static bool HasAnyRole(this ISecurityPrincipal principal, params string[] roles)
    {
        foreach (var role in roles)
        {
            if (principal.HasRole(role))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the principal holds all of the specified roles.
    /// </summary>
    public static bool HasAllRoles(this ISecurityPrincipal principal, params string[] roles)
    {
        foreach (var role in roles)
        {
            if (!principal.HasRole(role))
                return false;
        }
        return true;
    }
}
