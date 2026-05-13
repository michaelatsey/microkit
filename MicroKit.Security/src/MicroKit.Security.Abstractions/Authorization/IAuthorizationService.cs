using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Authorization;

/// <summary>Evaluates role and permission grants for an authenticated principal.</summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Returns true if the principal holds at least one of the specified permissions (OR logic).
    /// </summary>
    /// <param name="principal">The identity to evaluate.</param>
    /// <param name="permissions">Permissions to check — at least one must be present.</param>
    bool IsAuthorized(ISecurityPrincipal principal, params string[] permissions);

    /// <summary>
    /// Returns true if the principal holds ALL of the specified permissions.
    /// </summary>
    bool HasAllPermissions(ISecurityPrincipal principal, params string[] permissions);
}
