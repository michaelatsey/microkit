using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.Behaviors.Errors;

/// <summary>
/// One or more FluentValidation failures produced by
/// <see cref="ValidationBehavior{TRequest,TResponse}"/>.
/// All failures from every registered <c>IValidator&lt;TRequest&gt;</c> are collected
/// before short-circuiting — no early exit.
/// Pipeline order: <see cref="PipelineOrder.Validation"/> (300).
/// </summary>
/// <param name="Failures">All <see cref="ValidationFailure"/> instances. Never empty.</param>
public sealed record ValidationError(IReadOnlyList<ValidationFailure> Failures)
    : Error(ErrorCode.Validation, string.Join("; ", Failures.Select(f => f.ErrorMessage)))
{
    /// <inheritdoc />
    public override ErrorCategory Category => ErrorCategory.Validation;
}
