using MicroKit.Auth.Errors;
using MicroKit.Result;

namespace MicroKit.Auth.Multitenancy;

/// <summary>
/// Raised when tenant resolution from the authenticated user is attempted
/// but no authenticated user is present in the current execution scope.
/// </summary>
internal sealed record AuthMultitenancyUnauthenticatedError()
    : AuthError(
        ErrorCode.From("AUTH.MULTITENANCY.UNAUTHENTICATED"),
        "User is not authenticated.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}

/// <summary>
/// Raised when the authenticated user carries no tenant identifier,
/// so no tenant can be derived from their identity.
/// </summary>
internal sealed record AuthMultitenancyNoTenantError()
    : AuthError(
        ErrorCode.From("AUTH.MULTITENANCY.NO_TENANT"),
        "Authenticated user has no associated tenant.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}

/// <summary>Well-known errors produced by the Auth-to-Multitenancy bridge.</summary>
internal static class AuthMultitenancyErrors
{
    /// <summary>The current execution scope has no authenticated user.</summary>
    internal static readonly Error UserNotAuthenticated = new AuthMultitenancyUnauthenticatedError();

    /// <summary>The authenticated user has no associated tenant identifier.</summary>
    internal static readonly Error UserHasNoTenant = new AuthMultitenancyNoTenantError();
}
