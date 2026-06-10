namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Configurable <see cref="IPermissionChecker"/> and <see cref="ITenantPermissionChecker"/>
/// test double. Permissions not explicitly configured default to denied.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tenant-scope limitation:</b> both <c>HasPermissionAsync</c> overloads consult the same
/// internal allowed set regardless of the <c>tenantId</c> argument. Granting a permission
/// applies globally across all tenants — there is no per-tenant isolation.
/// </para>
/// <para>
/// For tests that need to assert tenant-scoped permission differences (e.g. user has
/// <c>audits:read</c> in tenant A but not in tenant B), use <see cref="FakePermissionStore"/>
/// wired to a real <c>PermissionEvaluator</c> (from <c>MicroKit.Auth</c>) instead.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Returns whether <paramref name="permission"/> was granted via <see cref="Allow"/>.
    /// </summary>
    /// <remarks>
    /// <b>The <paramref name="tenantId"/> argument is ignored.</b> This overload checks the
    /// same allowed set as the system-level overload. For tenant-scoped assertions use
    /// <see cref="FakePermissionStore"/> wired to a real <c>PermissionEvaluator</c>
    /// (from <c>MicroKit.Auth</c>).
    /// </remarks>
    public ValueTask<Result<bool>> HasPermissionAsync(
        Guid tenantId,
        Permission permission,
        CancellationToken ct = default)
        => new(Success(_allowed.Contains(permission)));
}
