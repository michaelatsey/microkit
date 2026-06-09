using MicroKit.Auth.Permissions;

namespace MicroKit.Auth.Roles;

/// <summary>
/// Configuration for the in-memory role-to-permission map registered by
/// <see cref="ServiceCollectionExtensions.AddInMemoryRoles"/>.
/// Defines which permissions are granted to each role, applied once at DI startup.
/// </summary>
/// <remarks>
/// Separate from <see cref="InMemoryRoleStoreOptions"/> — role assignment (who has a role)
/// and role-to-permission mapping (what a role grants) are distinct concerns.
/// <para>
/// <strong>Not thread-safe.</strong> Do not modify this object after the map singleton has
/// been resolved. All <see cref="Map"/> calls must occur within the single-threaded DI setup phase.
/// </para>
/// </remarks>
public sealed class InMemoryRolePermissionMapOptions
{
    private readonly Dictionary<Role, HashSet<Permission>> _rolePermissions = [];

    internal IReadOnlyDictionary<Role, HashSet<Permission>> RolePermissions => _rolePermissions;

    /// <summary>
    /// Maps the specified <paramref name="permissions"/> to <paramref name="role"/>.
    /// Calling this method multiple times for the same role accumulates permissions;
    /// duplicates are silently ignored.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="role">The role to configure. Must not be <see langword="null"/>.</param>
    /// <param name="permissions">The permissions granted by the role. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="InMemoryRolePermissionMapOptions"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="role"/> or <paramref name="permissions"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public InMemoryRolePermissionMapOptions Map(Role role, params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permissions);

        if (!_rolePermissions.TryGetValue(role, out var set))
        {
            set = [];
            _rolePermissions[role] = set;
        }

        foreach (var permission in permissions)
        {
            ArgumentNullException.ThrowIfNull(permission);
            set.Add(permission);
        }

        return this;
    }
}
