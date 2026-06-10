namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Configurable in-memory <see cref="IPermissionStore"/> test double.
/// Permissions not explicitly granted default to an empty list.
/// Supports both system-level and tenant-scoped grants.
/// </summary>
/// <example>
/// <code>
/// var store = new FakePermissionStore()
///     .Grant(userId, DocsPermissions.Read)
///     .GrantInTenant(userId, tenantId, DocsPermissions.Write);
/// </code>
/// </example>
public sealed class FakePermissionStore : IPermissionStore
{
    private readonly Dictionary<Guid, HashSet<Permission>> _systemPermissions = [];
    private readonly Dictionary<(Guid userId, Guid tenantId), HashSet<Permission>> _tenantPermissions = [];

    /// <summary>
    /// Grants a single system-level permission to the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="permission">The permission to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakePermissionStore Grant(Guid userId, Permission permission)
    {
        if (!_systemPermissions.TryGetValue(userId, out var set))
        {
            set = [];
            _systemPermissions[userId] = set;
        }

        set.Add(permission);
        return this;
    }

    /// <summary>
    /// Grants multiple system-level permissions to the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="permissions">The permissions to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakePermissionStore Grant(Guid userId, params Permission[] permissions)
    {
        foreach (var permission in permissions)
            Grant(userId, permission);

        return this;
    }

    /// <summary>
    /// Grants a single tenant-scoped permission to the specified user within the specified tenant.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="permission">The permission to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakePermissionStore GrantInTenant(Guid userId, Guid tenantId, Permission permission)
    {
        var key = (userId, tenantId);

        if (!_tenantPermissions.TryGetValue(key, out var set))
        {
            set = [];
            _tenantPermissions[key] = set;
        }

        set.Add(permission);
        return this;
    }

    /// <summary>
    /// Grants multiple tenant-scoped permissions to the specified user within the specified tenant.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="permissions">The permissions to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakePermissionStore GrantInTenant(Guid userId, Guid tenantId, params Permission[] permissions)
    {
        foreach (var permission in permissions)
            GrantInTenant(userId, tenantId, permission);

        return this;
    }

    /// <summary>
    /// Clears all granted permissions from both system-level and tenant-scoped stores.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakePermissionStore Clear()
    {
        _systemPermissions.Clear();
        _tenantPermissions.Clear();
        return this;
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var list = _systemPermissions.GetValueOrDefault(userId)?.ToList() ?? [];
        return new ValueTask<Result<IReadOnlyList<Permission>>>(
            Success<IReadOnlyList<Permission>>(list));
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var list = _tenantPermissions.GetValueOrDefault((userId, tenantId))?.ToList() ?? [];
        return new ValueTask<Result<IReadOnlyList<Permission>>>(
            Success<IReadOnlyList<Permission>>(list));
    }
}
