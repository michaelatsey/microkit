namespace MicroKit.Auth.Permissions;

/// <summary>
/// Configuration for the in-memory permission store registered by
/// <see cref="ServiceCollectionExtensions.AddInMemoryPermissions"/>.
/// Defines the static user-to-permission mappings applied once at DI startup.
/// </summary>
/// <remarks>
/// This object is populated once inside the factory lambda passed to
/// <see cref="ServiceCollectionExtensions.AddInMemoryPermissions"/> during DI configuration.
/// It is consumed immediately when the store singleton is first resolved from the container.
/// <para>
/// <strong>Not thread-safe.</strong> Do not modify this object after the store singleton has
/// been resolved. All calls to <see cref="Grant"/> and <see cref="GrantInTenant"/> must occur
/// within the single-threaded DI setup phase.
/// </para>
/// </remarks>
public sealed class InMemoryPermissionStoreOptions
{
    private readonly Dictionary<Guid, HashSet<Permission>> _systemGrants = [];
    private readonly Dictionary<(Guid UserId, Guid TenantId), HashSet<Permission>> _tenantGrants = [];

    internal IReadOnlyDictionary<Guid, HashSet<Permission>> SystemGrants => _systemGrants;
    internal IReadOnlyDictionary<(Guid UserId, Guid TenantId), HashSet<Permission>> TenantGrants => _tenantGrants;

    /// <summary>
    /// Grants the specified <paramref name="permissions"/> to <paramref name="userId"/> at system level
    /// (i.e., independent of any tenant context).
    /// Calling this method multiple times for the same user accumulates permissions; duplicates are silently ignored.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="userId">The user to grant permissions to.</param>
    /// <param name="permissions">The permissions to grant. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="InMemoryPermissionStoreOptions"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="permissions"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public InMemoryPermissionStoreOptions Grant(Guid userId, params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (!_systemGrants.TryGetValue(userId, out var set))
        {
            set = [];
            _systemGrants[userId] = set;
        }

        foreach (var permission in permissions)
        {
            ArgumentNullException.ThrowIfNull(permission);
            set.Add(permission);
        }

        return this;
    }

    /// <summary>
    /// Grants the specified <paramref name="permissions"/> to <paramref name="userId"/> scoped to
    /// <paramref name="tenantId"/>. These permissions are only returned when
    /// <see cref="IPermissionStore.GetPermissionsAsync(Guid, Guid, CancellationToken)"/> is called
    /// with the matching <c>(userId, tenantId)</c> pair.
    /// Calling this method multiple times for the same user + tenant pair accumulates permissions;
    /// duplicates are silently ignored.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="userId">The user to grant permissions to.</param>
    /// <param name="tenantId">The tenant scope for the grant.</param>
    /// <param name="permissions">The permissions to grant. Must not be <see langword="null"/>; individual elements must not be <see langword="null"/>.</param>
    /// <returns>This <see cref="InMemoryPermissionStoreOptions"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="permissions"/> is <see langword="null"/>,
    /// or when any element in the array is <see langword="null"/>.
    /// </exception>
    public InMemoryPermissionStoreOptions GrantInTenant(
        Guid userId,
        Guid tenantId,
        params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var key = (userId, tenantId);
        if (!_tenantGrants.TryGetValue(key, out var set))
        {
            set = [];
            _tenantGrants[key] = set;
        }

        foreach (var permission in permissions)
        {
            ArgumentNullException.ThrowIfNull(permission);
            set.Add(permission);
        }

        return this;
    }
}
