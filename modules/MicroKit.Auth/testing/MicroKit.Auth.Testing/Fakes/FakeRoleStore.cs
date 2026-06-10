namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Configurable in-memory <see cref="IRoleStore"/> test double.
/// Roles not explicitly granted default to an empty list.
/// Supports both system-level and tenant-scoped grants.
/// </summary>
/// <example>
/// <code>
/// var store = new FakeRoleStore()
///     .Grant(userId, Role.Of("admin"))
///     .GrantInTenant(userId, tenantId, Role.Of("auditor"));
/// </code>
/// </example>
public sealed class FakeRoleStore : IRoleStore
{
    private readonly Dictionary<Guid, HashSet<Role>> _systemRoles = [];
    private readonly Dictionary<(Guid userId, Guid tenantId), HashSet<Role>> _tenantRoles = [];

    /// <summary>
    /// Grants a single system-level role to the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="role">The role to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRoleStore Grant(Guid userId, Role role)
    {
        if (!_systemRoles.TryGetValue(userId, out var set))
        {
            set = [];
            _systemRoles[userId] = set;
        }

        set.Add(role);
        return this;
    }

    /// <summary>
    /// Grants multiple system-level roles to the specified user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roles">The roles to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRoleStore Grant(Guid userId, params Role[] roles)
    {
        foreach (var role in roles)
            Grant(userId, role);

        return this;
    }

    /// <summary>
    /// Grants a single tenant-scoped role to the specified user within the specified tenant.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="role">The role to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRoleStore GrantInTenant(Guid userId, Guid tenantId, Role role)
    {
        var key = (userId, tenantId);

        if (!_tenantRoles.TryGetValue(key, out var set))
        {
            set = [];
            _tenantRoles[key] = set;
        }

        set.Add(role);
        return this;
    }

    /// <summary>
    /// Grants multiple tenant-scoped roles to the specified user within the specified tenant.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="roles">The roles to grant.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRoleStore GrantInTenant(Guid userId, Guid tenantId, params Role[] roles)
    {
        foreach (var role in roles)
            GrantInTenant(userId, tenantId, role);

        return this;
    }

    /// <summary>
    /// Clears all granted roles from both system-level and tenant-scoped stores.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public FakeRoleStore Clear()
    {
        _systemRoles.Clear();
        _tenantRoles.Clear();
        return this;
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var list = _systemRoles.GetValueOrDefault(userId)?.ToList() ?? [];
        return new ValueTask<Result<IReadOnlyList<Role>>>(
            Success<IReadOnlyList<Role>>(list));
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var list = _tenantRoles.GetValueOrDefault((userId, tenantId))?.ToList() ?? [];
        return new ValueTask<Result<IReadOnlyList<Role>>>(
            Success<IReadOnlyList<Role>>(list));
    }
}
