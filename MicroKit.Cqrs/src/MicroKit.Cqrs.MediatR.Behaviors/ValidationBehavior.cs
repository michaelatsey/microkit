using FluentValidation;
using MediatR;
using ValidationException = FluentValidation.ValidationException;

namespace MicroKit.Cqrs.MediatR.Behaviors;

/// <summary>MediatR pipeline behavior that runs FluentValidation validators sequentially and fails on the first error.</summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="validators">The FluentValidation validators to run.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }

        return await next(cancellationToken);
    }
}
