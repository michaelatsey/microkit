namespace MicroKit.Auth.Roles;

/// <summary>
/// Configuration for the in-memory role store registered by
/// <see cref="ServiceCollectionExtensions.AddInMemoryRoles"/>.
/// Defines the static user-to-role assignments applied once at DI startup.
/// </summary>
/// <remarks>
/// This object is populated once inside the factory lambda passed to
/// <see cref="ServiceCollectionExtensions.AddInMemoryRoles"/> during DI configuration.
/// It is consumed immediately when the store singleton is first resolved from the container.
/// <para>
/// <strong>Not thread-safe.</strong> Do not modify this object after the store singleton has
/// been resolved. All calls to <see cref="Grant"/> and <see cref="GrantInTenant"/> must occur
/// within the single-threaded DI setup phase.
/// </para>
/// </remarks>
public sealed class InMemoryRoleStoreOptions
{
    private readonly Dictionary<Guid, HashSet<Role>> _systemGrants = [];
    private readonly Dictionary<(Guid UserId, Guid TenantId), HashSet<Role>> _tenantGrants = [];

    internal IReadOnlyDictionary<Guid, HashSet<Role>> SystemGrants => _systemGrants;
    internal IReadOnlyDictionary<(Guid UserId, Guid TenantId), HashSet<Role>> TenantGrants => _tenantGrants;

    /// <summary>
    /// Grants the specified <paramref name="roles"/> to <paramref name="userId"/> at system level
    /// (i.e., independent of any tenant context).
    /// Calling this method multiple times for the same user accumulates roles; duplicates are silently ignored.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="userId">The user to grant roles to.</param>
    /// <param name="roles">The roles to grant. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="InMemoryRoleStoreOptions"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="roles"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public InMemoryRoleStoreOptions Grant(Guid userId, params Role[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (!_systemGrants.TryGetValue(userId, out var set))
        {
            set = [];
            _systemGrants[userId] = set;
        }

        foreach (var role in roles)
        {
            ArgumentNullException.ThrowIfNull(role);
            set.Add(role);
        }

        return this;
    }

    /// <summary>
    /// Grants the specified <paramref name="roles"/> to <paramref name="userId"/> scoped to
    /// <paramref name="tenantId"/>. These roles are only returned when
    /// <see cref="IRoleStore.GetRolesAsync(Guid, Guid, CancellationToken)"/> is called
    /// with the matching <c>(userId, tenantId)</c> pair.
    /// Calling this method multiple times for the same user + tenant pair accumulates roles;
    /// duplicates are silently ignored.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="userId">The user to grant roles to.</param>
    /// <param name="tenantId">The tenant scope for the grant.</param>
    /// <param name="roles">The roles to grant. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="InMemoryRoleStoreOptions"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="roles"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public InMemoryRoleStoreOptions GrantInTenant(
        Guid userId,
        Guid tenantId,
        params Role[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var key = (userId, tenantId);
        if (!_tenantGrants.TryGetValue(key, out var set))
        {
            set = [];
            _tenantGrants[key] = set;
        }

        foreach (var role in roles)
        {
            ArgumentNullException.ThrowIfNull(role);
            set.Add(role);
        }

        return this;
    }
}
