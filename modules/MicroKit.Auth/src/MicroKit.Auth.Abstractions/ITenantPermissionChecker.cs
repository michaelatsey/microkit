namespace MicroKit.Auth;

/// <summary>
/// Checks whether the current user holds a permission within a specific tenant scope.
/// This is the preferred interface for multi-tenant authorization checks.
/// </summary>
/// <remarks>
/// Always prefer this interface over <see cref="IPermissionChecker"/> in multi-tenant
/// contexts. Explicitly passing the <c>tenantId</c> prevents accidental
/// cross-tenant permission elevation.
/// <para>
/// Implementations must not throw; all failure conditions are expressed as
/// <see cref="Result{T}"/> failures.
/// </para>
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface ITenantPermissionChecker
{
    /// <summary>
    /// Determines whether the current user has the specified permission within the given tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to scope the check to.</param>
    /// <param name="permission">The permission to check. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token. Callers should always pass a token; implementations must respect cancellation.</param>
    /// <returns>
    /// <c>Result&lt;bool&gt;.Success(true)</c> when the user holds the permission in the tenant;
    /// <c>Result&lt;bool&gt;.Success(false)</c> when the permission is not granted;
    /// <c>Result&lt;bool&gt;.Failure</c> with an <see cref="MicroKit.Auth.Errors.UnauthenticatedError"/>
    /// when no authenticated user is in scope.
    /// </returns>
    ValueTask<Result<bool>> HasPermissionAsync(Guid tenantId, Permission permission, CancellationToken ct = default);
}
