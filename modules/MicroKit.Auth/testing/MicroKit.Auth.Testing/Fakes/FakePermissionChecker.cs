namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Configurable <see cref="IPermissionChecker"/> and <see cref="ITenantPermissionChecker"/>
/// test double. Permissions not explicitly configured default to denied.
/// </summary>
/// <example>
/// <code>
/// var checker = new FakePermissionChecker()
///     .Allow(AuditPermissions.Read)
///     .Deny(AuditPermissions.Delete);
/// </code>
/// </example>
public sealed class FakePermissionChecker : IPermissionChecker, ITenantPermissionChecker
{
    private readonly HashSet<Permission> _allowed = [];

    /// <summary>Configures <paramref name="permission"/> to be granted.</summary>
    public FakePermissionChecker Allow(Permission permission)
    {
        _allowed.Add(permission);
        return this;
    }

    /// <summary>Removes <paramref name="permission"/> from the allowed set (explicit deny).</summary>
    public FakePermissionChecker Deny(Permission permission)
    {
        _allowed.Remove(permission);
        return this;
    }

    /// <inheritdoc />
    public ValueTask<Result<bool>> HasPermissionAsync(
        Permission permission,
        CancellationToken ct = default)
        => new(Success(_allowed.Contains(permission)));

    /// <inheritdoc />
    public ValueTask<Result<bool>> HasPermissionAsync(
        Guid tenantId,
        Permission permission,
        CancellationToken ct = default)
        => new(Success(_allowed.Contains(permission)));
}
