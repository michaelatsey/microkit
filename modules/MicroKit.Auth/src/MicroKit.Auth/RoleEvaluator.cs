namespace MicroKit.Auth;

/// <summary>
/// Core RBAC engine for role membership. Implements both <see cref="IRoleChecker"/>
/// (system-level) and <see cref="ITenantRoleChecker"/> (tenant-scoped). Register as a single
/// Scoped instance via <see cref="ServiceCollectionExtensions.AddMicroKitAuthCore"/> so both
/// interfaces share the same instance per scope.
/// </summary>
/// <remarks>
/// Role membership is evaluated in two steps:
/// <list type="number">
///   <item>JWT roles — check <see cref="ICurrentUser.Roles"/> populated from the token claims.</item>
///   <item>Store roles — fall through to <see cref="IRoleStore"/> for dynamically assigned roles.</item>
/// </list>
/// JWT role check is always attempted first to avoid an unnecessary store round-trip.
/// </remarks>
/// <param name="accessor">Provides the current user for this execution scope.</param>
/// <param name="store">Retrieves the role set for a given user / tenant pair.</param>
public sealed class RoleEvaluator(ICurrentUserAccessor accessor, IRoleStore store)
    : IRoleChecker, ITenantRoleChecker
{
    /// <inheritdoc />
    public async ValueTask<Result<bool>> HasRoleAsync(Role role, CancellationToken ct = default)
    {
        var user = accessor.Get();
        if (user is null || !user.IsAuthenticated)
            return Failure<bool>(new UnauthenticatedError());

        if (user.Roles.Any(r => r == role))
            return Success(true);

        var storeResult = await store
            .GetRolesAsync(user.UserId, ct)
            .ConfigureAwait(false);

        if (storeResult.IsFailure)
            return Failure<bool>(storeResult.Error);

        return Success(storeResult.Value.Any(r => r == role));
    }

    /// <inheritdoc />
    public async ValueTask<Result<bool>> HasRoleAsync(
        Guid tenantId,
        Role role,
        CancellationToken ct = default)
    {
        var user = accessor.Get();
        if (user is null || !user.IsAuthenticated)
            return Failure<bool>(new UnauthenticatedError());

        if (user.Roles.Any(r => r == role))
            return Success(true);

        var storeResult = await store
            .GetRolesAsync(user.UserId, tenantId, ct)
            .ConfigureAwait(false);

        if (storeResult.IsFailure)
            return Failure<bool>(storeResult.Error);

        return Success(storeResult.Value.Any(r => r == role));
    }
}
