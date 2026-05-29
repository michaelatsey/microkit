using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.Behaviors.Errors;

/// <summary>
/// Authorization failure produced by <see cref="AuthorizationBehavior{TRequest,TResponse}"/>
/// when one or more required ASP.NET Core authorization policies are not satisfied.
/// Pipeline order: <see cref="PipelineOrder.Authorization"/> (200).
/// </summary>
/// <param name="PolicyName">The name of the first policy that failed.</param>
public sealed record UnauthorizedError(string PolicyName)
    : Error(ErrorCode.Unauthorized, $"Authorization policy '{PolicyName}' was not satisfied.")
{
    /// <inheritdoc />
    public override ErrorCategory Category => ErrorCategory.Unauthorized;
}
