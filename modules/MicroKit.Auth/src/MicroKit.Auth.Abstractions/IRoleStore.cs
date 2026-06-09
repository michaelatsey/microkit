namespace MicroKit.Auth;

/// <summary>
/// Retrieves the set of roles assigned to a specific user.
/// This is an infrastructure-facing contract. Implementations reside in
/// <c>MicroKit.Auth.Roles</c> or a provider-specific package.
/// </summary>
/// <remarks>
/// Consumers of the authorization layer should use <see cref="IRoleChecker"/>
/// or <see cref="ITenantRoleChecker"/> rather than this store directly.
/// The store is called by the role checker implementations to resolve
/// roles from the backing data source.
/// <para>All async implementations must use <c>ConfigureAwait(false)</c>.</para>
/// </remarks>
public interface IRoleStore
{
    /// <summary>
    /// Retrieves all roles granted to the specified user within the specified tenant.
    /// Use this overload in multi-tenant contexts.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="tenantId">The tenant scope for which to retrieve roles.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;IReadOnlyList&lt;Role&gt;&gt;.Success</c> with the list of granted roles
    /// (empty list when none are granted);
    /// <c>Result.Failure</c> on infrastructure or data access errors.
    /// </returns>
    ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all system-level roles granted to the specified user, without tenant scoping.
    /// Use this overload for <see cref="IRoleChecker"/> implementations operating in
    /// system-level or single-tenant contexts.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <c>Result&lt;IReadOnlyList&lt;Role&gt;&gt;.Success</c> with the list of granted roles
    /// (empty list when none are granted);
    /// <c>Result.Failure</c> on infrastructure or data access errors.
    /// </returns>
    ValueTask<Result<IReadOnlyList<Role>>> GetRolesAsync(
        Guid userId,
        CancellationToken ct = default);
}
