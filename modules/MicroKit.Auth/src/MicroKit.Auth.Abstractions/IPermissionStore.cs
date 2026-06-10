namespace MicroKit.Auth;

/// <summary>
/// Retrieves the set of permissions granted to a specific user.
/// This is an infrastructure-facing contract. Implementations reside in
/// <c>MicroKit.Auth.Permissions</c> or a provider-specific package.
/// </summary>
/// <remarks>
/// Consumers of the authorization layer should use <see cref="IPermissionChecker"/>
/// or <see cref="ITenantPermissionChecker"/> rather than this store directly.
/// The store is called by the permission checker implementations to resolve
/// permissions from the backing data source.
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IPermissionStore
{
    /// <summary>
    /// Retrieves all permissions granted to the specified user within the specified tenant.
    /// Use this overload in multi-tenant contexts.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="tenantId">The tenant scope for which to retrieve permissions.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;IReadOnlyList&lt;Permission&gt;&gt;.Success</c> with the list of granted permissions
    /// (empty list when none are granted);
    /// <c>Result.Failure</c> on infrastructure or data access errors.
    /// </returns>
    ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all system-level permissions granted to the specified user, without tenant scoping.
    /// Use this overload for <see cref="IPermissionChecker"/> implementations operating in
    /// system-level or single-tenant contexts.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;IReadOnlyList&lt;Permission&gt;&gt;.Success</c> with the list of granted permissions
    /// (empty list when none are granted);
    /// <c>Result.Failure</c> on infrastructure or data access errors.
    /// </returns>
    ValueTask<Result<IReadOnlyList<Permission>>> GetPermissionsAsync(
        Guid userId,
        CancellationToken ct = default);
}
