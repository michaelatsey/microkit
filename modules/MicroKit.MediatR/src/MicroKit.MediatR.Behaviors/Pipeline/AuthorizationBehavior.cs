using System.Security.Claims;
using MicroKit.MediatR.Behaviors.Errors;

namespace MicroKit.MediatR.Behaviors.Pipeline;

/// <summary>
/// Evaluates all ASP.NET Core authorization policies declared in
/// <see cref="IAuthorizedRequest.RequiredPolicies"/> before the handler executes.
/// Short-circuits with <see cref="UnauthenticatedError"/> when no user context is available,
/// or with <see cref="UnauthorizedError"/> on the first failing policy.
/// Pipeline order: <see cref="PipelineOrder.Authorization"/> (200).
/// </summary>
/// <remarks>
/// Requires <c>IAuthorizationService</c> and <see cref="ICurrentUserAccessor"/> in DI.
/// For non-<c>Result&lt;T&gt;</c> responses, throws <see cref="UnauthorizedAccessException"/>
/// on failure instead of returning a failure result.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class AuthorizationBehavior<TRequest, TResponse>(
    IAuthorizationService authorizationService,
    ICurrentUserAccessor userAccessor)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public override int Order => PipelineOrder.Authorization;

    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IAuthorizedRequest authorizedRequest)
            return await next().ConfigureAwait(false);

        var principal = userAccessor.Current;
        if (principal is null)
            return CreateFailureOrThrow(
                new UnauthenticatedError(),
                new UnauthorizedAccessException("No authenticated user context is available."));

        foreach (var policy in authorizedRequest.RequiredPolicies)
        {
            var result = await authorizationService
                .AuthorizeAsync(principal, resource: null, policy)
                .ConfigureAwait(false);

            if (!result.Succeeded)
                return CreateFailureOrThrow(
                    new UnauthorizedError(policy),
                    new UnauthorizedAccessException($"Authorization policy '{policy}' was not satisfied."));
        }

        return await next().ConfigureAwait(false);
    }
}
