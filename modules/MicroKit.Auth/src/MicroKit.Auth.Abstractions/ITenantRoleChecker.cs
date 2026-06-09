namespace MicroKit.Auth;

/// <summary>
/// Checks whether the current user holds a role within a specific tenant scope.
/// This is the preferred interface for multi-tenant role checks.
/// </summary>
/// <remarks>
/// Always prefer this interface over <see cref="IRoleChecker"/> in multi-tenant
/// contexts. Explicitly passing the <c>tenantId</c> prevents accidental
/// cross-tenant role elevation.
/// <para>
/// Implementations must not throw; all failure conditions are expressed as
/// <see cref="Result{T}"/> failures.
/// </para>
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface ITenantRoleChecker
{
    /// <summary>
    /// Determines whether the current user belongs to the specified role within the given tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to scope the check to.</param>
    /// <param name="role">The role to check. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token. Callers should always pass a token; implementations must respect cancellation.</param>
    /// <returns>
    /// <c>Result&lt;bool&gt;.Success(true)</c> when the user holds the role in the tenant;
    /// <c>Result&lt;bool&gt;.Success(false)</c> when the role is not held;
    /// <c>Result&lt;bool&gt;.Failure</c> with an <see cref="MicroKit.Auth.Errors.UnauthenticatedError"/>
    /// when no authenticated user is in scope.
    /// </returns>
    ValueTask<Result<bool>> HasRoleAsync(Guid tenantId, Role role, CancellationToken ct = default);
}
