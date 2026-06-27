using MicroKit.Auth;
using MicroKit.Tenancy;
using MicroKit.Result;

namespace MicroKit.Auth.Multitenancy;

/// <summary>
/// Derives the current tenant from the authenticated user's identity.
/// Implements <see cref="ITenantResolutionStrategy"/> to compose additively into
/// the MicroKit.Tenancy resolution pipeline via <see cref="Order"/>.
/// Returns a failure <see cref="Result{TenantId}"/> if the user is unauthenticated
/// or carries no tenant ID claim — the pipeline proceeds to the next strategy.
/// </summary>
public sealed class AuthTenantResolutionStrategy : ITenantResolutionStrategy
{
    private readonly ICurrentUserAccessor _userAccessor;

    /// <summary>Initializes a new instance of <see cref="AuthTenantResolutionStrategy"/>.</summary>
    /// <param name="userAccessor">Provides access to the current authenticated user.</param>
    public AuthTenantResolutionStrategy(ICurrentUserAccessor userAccessor)
    {
        ArgumentNullException.ThrowIfNull(userAccessor);
        _userAccessor = userAccessor;
    }

    /// <summary>
    /// Resolution order. Value <c>40</c> places this strategy after built-in HTTP strategies
    /// (header: 10, route: 20, subdomain: 30) and before host-mapping (50).
    /// </summary>
    public int Order => 40;

    /// <summary>
    /// Attempts to resolve a <see cref="TenantId"/> from the authenticated user's claims.
    /// Returns failure without throwing if the user is absent, unauthenticated, or has no tenant.
    /// </summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A successful <see cref="Result{TenantId}"/> containing the tenant on success,
    /// or a failure result if the user is unauthenticated or has no tenant ID.
    /// </returns>
    public ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        var user = _userAccessor.Get();

        if (user is null || !user.IsAuthenticated)
            return ValueTask.FromResult(Result<TenantId>.Failure(AuthMultitenancyErrors.UserNotAuthenticated));

        if (user.TenantId is null)
            return ValueTask.FromResult(Result<TenantId>.Failure(AuthMultitenancyErrors.UserHasNoTenant));

        return ValueTask.FromResult(Result<TenantId>.Success(new TenantId(user.TenantId.Value)));
    }
}
