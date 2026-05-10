using MicroKit.Security.Abstractions.Constants;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Extensions;

/// <summary>
/// Extensions pour ISecurityPrincipal fournissant des méthodes d'accès pratiques.
/// </summary>
public static class SecurityPrincipalExtensions
{
    /// <summary>
    /// Récupère l'adresse email du principal.
    /// </summary>
    public static string? GetEmail(this ISecurityPrincipal principal) =>
        principal.GetClaimValue(ClaimTypes.Email);

    /// <summary>
    /// Récupère l'identifiant du tenant.
    /// </summary>
    public static string? GetTenantId(this ISecurityPrincipal principal) =>
        principal.GetClaimValue(ClaimTypes.TenantId);

    /// <summary>
    /// Récupère l'identifiant du client.
    /// </summary>
    public static string? GetClientId(this ISecurityPrincipal principal) =>
        principal.GetClaimValue(ClaimTypes.ClientId);

    /// <summary>
    /// Vérifie si le principal possède un rôle spécifique.
    /// </summary>
    public static bool HasRole(this ISecurityPrincipal principal, string role) =>
        principal.HasClaim(ClaimTypes.Role, role) ||
        principal.HasClaim(ClaimTypes.Roles, role);

    /// <summary>
    /// Vérifie si le principal possède une permission spécifique.
    /// </summary>
    public static bool HasPermission(this ISecurityPrincipal principal, string permission) =>
        principal.HasClaim(ClaimTypes.Permissions, permission);

    /// <summary>
    /// Récupère tous les rôles du principal.
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
    /// Récupère toutes les permissions du principal.
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
    /// Vérifie si le principal possède au moins un des rôles spécifiés.
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
    /// Vérifie si le principal possède tous les rôles spécifiés.
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
