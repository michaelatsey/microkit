using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.Behaviors.Errors;

/// <summary>
/// Produced by <see cref="AuthorizationBehavior{TRequest,TResponse}"/> when no authenticated
/// user context is available (i.e., <see cref="ICurrentUserAccessor.Current"/> is <see langword="null"/>).
/// </summary>
/// <remarks>
/// Distinct from <see cref="UnauthorizedError"/>, which signals that a specific authorization
/// policy was not satisfied for an authenticated user. <see cref="UnauthenticatedError"/> signals
/// that no user identity could be established at all — authentication has not occurred.
/// </remarks>
public sealed record UnauthenticatedError()
    : Error(ErrorCode.Unauthorized, "No authenticated user context is available.")
{
    /// <inheritdoc />
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}
