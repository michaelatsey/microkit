namespace MicroKit.Auth;

/// <summary>
/// Checks whether the current user holds a specific permission.
/// This interface operates without an explicit tenant context and is appropriate
/// for system-level operations or single-tenant deployments.
/// </summary>
/// <remarks>
/// In multi-tenant scenarios, prefer <see cref="ITenantPermissionChecker"/> to ensure
/// permission evaluation is scoped to the correct tenant. Cross-tenant evaluation
/// is explicitly forbidden without a deliberate justification comment.
/// <para>
/// Implementations must not throw; all failure conditions are expressed as
/// <see cref="Result{T}"/> failures.
/// </para>
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IPermissionChecker
{
    /// <summary>
    /// Determines whether the current user has the specified permission.
    /// </summary>
    /// <param name="permission">The permission to check. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token. Callers should always pass a token; implementations must respect cancellation.</param>
    /// <returns>
    /// <c>Result&lt;bool&gt;.Success(true)</c> when the user holds the permission;
    /// <c>Result&lt;bool&gt;.Success(false)</c> when the permission is not granted;
    /// <c>Result&lt;bool&gt;.Failure</c> with an <see cref="MicroKit.Auth.Errors.UnauthenticatedError"/>
    /// when no authenticated user is in scope.
    /// </returns>
    ValueTask<Result<bool>> HasPermissionAsync(Permission permission, CancellationToken ct = default);
}
