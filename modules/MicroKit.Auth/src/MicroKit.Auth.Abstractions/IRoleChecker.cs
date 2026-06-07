namespace MicroKit.Auth;

/// <summary>
/// Checks whether the current user is a member of a specific role.
/// </summary>
/// <remarks>
/// In domain logic, prefer <see cref="IPermissionChecker"/> over direct role checks.
/// Role checks expose internal role structure to callers; permission checks express
/// intent more clearly and decouple the caller from role names.
/// Use <c>IRoleChecker</c> only when a role identity is semantically meaningful to
/// the caller (e.g. admin-only UI panels).
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IRoleChecker
{
    /// <summary>
    /// Determines whether the current user belongs to the specified role.
    /// </summary>
    /// <param name="role">The role to check. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;bool&gt;.Success(true)</c> when the user has the role;
    /// <c>Result&lt;bool&gt;.Success(false)</c> when the role is not held;
    /// <c>Result&lt;bool&gt;.Failure</c> with an <see cref="MicroKit.Auth.Errors.UnauthenticatedError"/>
    /// when no authenticated user is in scope.
    /// </returns>
    ValueTask<Result<bool>> HasRoleAsync(Role role, CancellationToken ct = default);
}
