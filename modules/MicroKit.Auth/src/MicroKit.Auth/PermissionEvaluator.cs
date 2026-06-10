namespace MicroKit.Auth;

/// <summary>
/// Core RBAC engine. Implements both <see cref="IPermissionChecker"/> (system-level) and
/// <see cref="ITenantPermissionChecker"/> (tenant-scoped). Register as a single Scoped
/// instance via <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/> so both
/// interfaces share the same instance per scope.
/// </summary>
/// <remarks>
/// <para><b>Evaluation strategy — single pass per permission source:</b></para>
/// <list type="number">
///   <item>Unauthenticated or missing user → <c>Result.Failure(UnauthenticatedError)</c>.</item>
///   <item>SuperAdmin role present → <c>true</c> (bypasses all further checks).</item>
///   <item>Store permissions (direct grants) — one pass through <see cref="IPermissionStore"/>
///         results: exact match OR wildcard (<c>resource:*</c>, <c>*:action</c>, <c>*:*</c>)
///         evaluated together in a single scan.</item>
///   <item>Role permissions — one pass per role from <see cref="IRolePermissionMap"/>:
///         same exact + wildcard logic applied to each role's grant set.</item>
///   <item>No source matched → <c>false</c>.</item>
/// </list>
/// <para>
/// Within each source (store or role), exact and wildcard matches are resolved in a single
/// scan — there is no separate wildcard-only pass. The conceptual "5-step sequence" describes
/// source priority (store before roles), not the matching algorithm within a source.
/// </para>
/// </remarks>
/// <param name="accessor">Provides the current user for this execution scope.</param>
/// <param name="store">Retrieves the permission set for a given user / tenant pair.</param>
/// <param name="roleMap">Maps roles to their granted permissions for role-based permission expansion.</param>
public sealed class PermissionEvaluator(
    ICurrentUserAccessor accessor,
    IPermissionStore store,
    IRolePermissionMap roleMap)
    : IPermissionChecker, ITenantPermissionChecker
{
    private const string SuperAdminRoleName = "superadmin";

    /// <inheritdoc />
    public async ValueTask<Result<bool>> HasPermissionAsync(
        Permission permission,
        CancellationToken ct = default)
    {
        var user = accessor.Get();
        if (user is null || !user.IsAuthenticated)
            return Failure<bool>(new UnauthenticatedError());

        if (IsSuperAdmin(user))
            return Success(true);

        var storeResult = await store
            .GetPermissionsAsync(user.UserId, ct)
            .ConfigureAwait(false);

        if (storeResult.IsFailure)
            return Failure<bool>(storeResult.Error);

        if (Matches(storeResult.Value, permission))
            return Success(true);

        foreach (var role in user.Roles)
        {
            if (Matches(roleMap.GetPermissionsForRole(role), permission))
                return Success(true);
        }

        return Success(false);
    }

    /// <inheritdoc />
    public async ValueTask<Result<bool>> HasPermissionAsync(
        Guid tenantId,
        Permission permission,
        CancellationToken ct = default)
    {
        var user = accessor.Get();
        if (user is null || !user.IsAuthenticated)
            return Failure<bool>(new UnauthenticatedError());

        if (IsSuperAdmin(user))
            return Success(true);

        var storeResult = await store
            .GetPermissionsAsync(user.UserId, tenantId, ct)
            .ConfigureAwait(false);

        if (storeResult.IsFailure)
            return Failure<bool>(storeResult.Error);

        if (Matches(storeResult.Value, permission))
            return Success(true);

        foreach (var role in user.Roles)
        {
            if (Matches(roleMap.GetPermissionsForRole(role), permission))
                return Success(true);
        }

        return Success(false);
    }

    private static bool IsSuperAdmin(ICurrentUser user) =>
        user.Roles.Any(r => r.Name == SuperAdminRoleName);

    /// <summary>
    /// Checks whether <paramref name="required"/> is satisfied by any entry in
    /// <paramref name="granted"/>. Supports exact match and wildcard patterns:
    /// <c>resource:*</c>, <c>*:action</c>, <c>*:*</c>.
    /// </summary>
    private static bool Matches(IReadOnlyList<Permission> granted, Permission required)
    {
        foreach (var p in granted)
        {
            if (p == required) return true;
            if (p.Resource == required.Resource && p.Action == "*") return true;
            if (p.Resource == "*" && p.Action == required.Action) return true;
            if (p.Resource == "*" && p.Action == "*") return true;
        }
        return false;
    }
}
