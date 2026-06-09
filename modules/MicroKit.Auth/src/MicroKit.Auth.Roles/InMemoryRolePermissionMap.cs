using MicroKit.Auth.Permissions;

namespace MicroKit.Auth.Roles;

/// <summary>
/// In-memory <see cref="IRolePermissionMap"/> backed by static role-to-permission mappings
/// defined in <see cref="InMemoryRolePermissionMapOptions"/>.
/// </summary>
/// <remarks>
/// <strong>Internal constructor — use DI only.</strong> Instantiate this map exclusively via
/// <see cref="ServiceCollectionExtensions.AddInMemoryRoles"/> with the <c>configureMap</c> parameter.
/// <para>
/// Returns an empty list for any role not explicitly configured.
/// Never returns <see langword="null"/>.
/// </para>
/// </remarks>
internal sealed class InMemoryRolePermissionMap : IRolePermissionMap
{
    private static readonly IReadOnlyList<Permission> Empty = Array.Empty<Permission>();

    private readonly InMemoryRolePermissionMapOptions _options;

    internal InMemoryRolePermissionMap(InMemoryRolePermissionMapOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public IReadOnlyList<Permission> GetPermissionsForRole(Role role)
    {
        if (_options.RolePermissions.TryGetValue(role, out var set))
            return set.ToList();

        return Empty;
    }
}
