namespace MicroKit.Auth.Errors;

/// <summary>
/// Raised when an operation requires an authenticated user but no authenticated
/// user is present in the current execution scope.
/// </summary>
public sealed record UnauthenticatedError()
    : AuthError(
        ErrorCode.From("AUTH.USER.UNAUTHENTICATED"),
        "No authenticated user is present in the current scope.")
{
    /// <inheritdoc/>
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}
