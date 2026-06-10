namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Configurable in-memory <see cref="IRolePermissionMap"/> test double.
/// Roles without an explicit mapping return an empty permission list.
/// </summary>
/// <example>
/// <code>
/// var map = new FakeRolePermissionMap()
///     .Map(Role.Of("admin"), DocsPermissions.Read, DocsPermissions.Write)
///     .Map(Role.Of("viewer"), DocsPermissions.Read);
/// </code>
/// </example>
public sealed class FakeRolePermissionMap : IRolePermissionMap
{
    private readonly Dictionary<Role, HashSet<Permission>> _map = [];

    /// <summary>
    /// Maps a single permission to the specified role.
    /// </summary>
    /// <param name="role">The role to configure.</param>
    /// <param name="permission">The permission to grant to the role.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRolePermissionMap Map(Role role, Permission permission)
    {
        if (!_map.TryGetValue(role, out var set))
        {
            set = [];
            _map[role] = set;
        }

        set.Add(permission);
        return this;
    }

    /// <summary>
    /// Maps multiple permissions to the specified role.
    /// </summary>
    /// <param name="role">The role to configure.</param>
    /// <param name="permissions">The permissions to grant to the role.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRolePermissionMap Map(Role role, params Permission[] permissions)
    {
        foreach (var permission in permissions)
            Map(role, permission);

        return this;
    }

    /// <summary>
    /// Clears all role-to-permission mappings.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRolePermissionMap Clear()
    {
        _map.Clear();
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<Permission> GetPermissionsForRole(Role role)
        => _map.TryGetValue(role, out var set)
            ? set.ToList()
            : [];
}
