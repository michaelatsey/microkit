namespace MicroKit.Auth;

/// <summary>
/// Maps a <see cref="Role"/> to the set of <see cref="Permission"/> values it grants.
/// Used by <see cref="IPermissionChecker"/> implementations to expand role-based permissions
/// during permission evaluation (step 3 of the evaluation order).
/// </summary>
/// <remarks>
/// This interface is synchronous — role-to-permission mapping is static configuration
/// resolved once at DI startup, not a data-access operation.
/// <para>
/// Phase 1 treats role-to-permission mapping as tenant-agnostic.
/// A tenant-scoped overload may be added in Phase 2 if per-tenant role customisation is required.
/// </para>
/// </remarks>
public interface IRolePermissionMap
{
    /// <summary>
    /// Returns all permissions granted by the specified <paramref name="role"/>.
    /// Returns an empty list when the role has no configured permission grants.
    /// Never returns <see langword="null"/>.
    /// </summary>
    /// <param name="role">The role to look up. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A read-only list of permissions granted to the role.
    /// Empty when no mapping is configured for the role.
    /// </returns>
    IReadOnlyList<Permission> GetPermissionsForRole(Role role);
}
