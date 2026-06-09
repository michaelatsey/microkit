namespace MicroKit.Auth.Roles;

/// <summary>
/// In-memory <see cref="IRoleStore"/> backed by static user-to-role mappings
/// defined in <see cref="InMemoryRoleStoreOptions"/>.
/// Suitable for static role configurations, early-phase projects, and test scenarios
/// where a real data store is not available.
/// </summary>
/// <remarks>
/// <strong>Internal constructor — use DI only.</strong> Instantiate this store exclusively via
/// <see cref="ServiceCollectionExtensions.AddInMemoryRoles"/>. The <c>internal</c> access
/// enforces that consumers always obtain the store from the DI container rather than constructing
/// it directly with <c>new</c>, ensuring the options are correctly provided through the registered
/// factory and that the singleton lifecycle is respected.
/// <para>
/// Returns <see cref="Result{T}.IsSuccess"/> with an empty list when no roles are configured
/// for a user (or user + tenant pair) — effectively a deny-all state for unknown users.
/// Never returns <see cref="Result{T}.IsFailure"/> from <see cref="GetRolesAsync(Guid, CancellationToken)"/>
/// or <see cref="GetRolesAsync(Guid, Guid, CancellationToken)"/>.
/// </para>
/// </remarks>
internal sealed class InMemoryRoleStore : IRoleStore
{
    private static readonly IReadOnlyList<Role> Empty = Array.Empty<Role>();

    private readonly InMemoryRoleStoreOptions _options;

    internal InMemoryRoleStore(InMemoryRoleStoreOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var key = (userId, tenantId);
        if (_options.TenantGrants.TryGetValue(key, out var set))
        {
            IReadOnlyList<Role> found = set.ToList();
            return new(Success(found));
        }

        return new(Success(Empty));
    }

    /// <inheritdoc />
    public ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (_options.SystemGrants.TryGetValue(userId, out var set))
        {
            IReadOnlyList<Role> found = set.ToList();
            return new(Success(found));
        }

        return new(Success(Empty));
    }
}
