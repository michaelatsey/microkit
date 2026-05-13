
using MicroKit.Security.Abstractions.Authorization;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace MicroKit.Security.Core.Authorization;

/// <summary>Default <see cref="IAuthorizationService"/> implementation that evaluates claims from configured claim-type mappings.</summary>
public sealed class AuthorizationService(
    IOptions<SecurityOptions> options,
    ILogger<AuthorizationService> logger) : IAuthorizationService
{
    private readonly SecurityOptions _options = options.Value;

    /// <inheritdoc />
    public bool IsAuthorized(ISecurityPrincipal principal, params string[] permissions)
    {
        if (!principal.IsAuthenticated) return false;

        ReadOnlySpan<string> permsSpan = permissions;
        if (permsSpan.IsEmpty) return true;

        // OR logic: at least one permission must match.
        foreach (var permission in permissions)
        {
            if (HasPermission(principal, permission))
            {
                return true;
            }
        }

        logger.LogWarning("Authorization failed for {PrincipalId}. Missing any of: {Permissions}",
            principal.Identifier, string.Join(", ", permissions));

        return false;
    }

    /// <inheritdoc />
    public bool HasAllPermissions(ISecurityPrincipal principal, params string[] permissions)
    {
        if (!principal.IsAuthenticated) return false;

        ReadOnlySpan<string> permsSpan = permissions;
        if (permsSpan.IsEmpty) return true;

        // AND logic: every permission must match.
        foreach (var permission in permsSpan)
        {
            if (!HasPermission(principal, permission))
            {
                logger.LogWarning("Authorization failed for {PrincipalId}. Missing required permission: {Permission}",
                    principal.Identifier, permission);
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasPermission(ISecurityPrincipal principal, string permission)
    {
        var mapping = _options.ClaimsMapping;
        return principal.HasClaim(mapping.PermissionClaim, permission) ||
               principal.HasClaim(mapping.ScopeClaim, permission) ||
               principal.HasClaim(mapping.RoleClaim, permission);
    }
}
