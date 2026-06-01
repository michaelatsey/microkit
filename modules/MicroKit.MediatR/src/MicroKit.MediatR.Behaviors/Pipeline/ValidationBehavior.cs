namespace MicroKit.MediatR.Behaviors.Pipeline;

/// <summary>
/// Executes all registered <c>IValidator&lt;TRequest&gt;</c> instances and collects
/// every failure before short-circuiting. Zero-cost pass-through when no validators
/// are registered — checked once at construction, not per request.
/// Pipeline order: <see cref="PipelineOrder.Validation"/> (300).
/// </summary>
/// <remarks>
/// Activation does not require a marker interface — registering an
/// <c>IValidator&lt;TRequest&gt;</c> in DI is the opt-in signal.
/// All validators run and all failures are collected; there is no early exit on first failure.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    // Materialized once at construction — _validators.Length == 0 is a single int comparison per request.
    private readonly IValidator<TRequest>[] _validators = validators.ToArray();

    /// <inheritdoc />
    public override int Order => PipelineOrder.Validation;

    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
            return await next().ConfigureAwait(false);

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<ValidationFailure>();

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
                failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            return CreateFailureOrThrow(
                new Errors.ValidationError(failures),
                new ValidationException(failures));

        return await next().ConfigureAwait(false);
    }
}
