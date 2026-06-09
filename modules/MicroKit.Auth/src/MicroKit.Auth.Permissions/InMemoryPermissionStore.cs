namespace MicroKit.Auth.Permissions;

/// <summary>
/// In-memory <see cref="IPermissionStore"/> backed by static user-to-permission mappings
/// defined in <see cref="InMemoryPermissionStoreOptions"/>.
/// Suitable for static permission configurations, early-phase projects, and test scenarios
/// where a real data store is not available.
/// </summary>
/// <remarks>
/// <strong>Internal constructor — use DI only.</strong> Instantiate this store exclusively via
/// <see cref="ServiceCollectionExtensions.AddInMemoryPermissions"/>. The <c>internal</c> access
/// enforces that consumers always obtain the store from the DI container rather than constructing
/// it directly with <c>new</c>, ensuring the options are correctly provided through the registered
/// factory and that the singleton lifecycle is respected.
/// <para>
/// Returns <see cref="Result{T}.IsSuccess"/> with an empty list when no permissions are configured
/// for a user (or user + tenant pair) — effectively a deny-all state for unknown users.
/// Never returns <see cref="Result{T}.IsFailure"/> from <see cref="GetPermissionsAsync(Guid, CancellationToken)"/>
/// or <see cref="GetPermissionsAsync(Guid, Guid, CancellationToken)"/>.
/// </para>
/// </remarks>
internal sealed class InMemoryPermissionStore : IPermissionStore
{
    private static readonly IReadOnlyList<Permission> Empty = Array.Empty<Permission>();

    private readonly InMemoryPermissionStoreOptions _options;

    internal InMemoryPermissionStore(InMemoryPermissionStoreOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var key = (userId, tenantId);
        if (_options.TenantGrants.TryGetValue(key, out var set))
        {
            IReadOnlyList<Permission> found = set.ToList();
            return new(Success(found));
        }

        return new(Success(Empty));
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (_options.SystemGrants.TryGetValue(userId, out var set))
        {
            IReadOnlyList<Permission> found = set.ToList();
            return new(Success(found));
        }

        return new(Success(Empty));
    }
}
