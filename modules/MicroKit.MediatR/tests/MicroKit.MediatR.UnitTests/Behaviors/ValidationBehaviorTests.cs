using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.MediatR.Behaviors.Pipeline;
using MicroKit.Result;
using static MicroKit.Result.Result;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class ValidationBehaviorTests
{
    // Concrete AbstractValidator<T> subclasses are used instead of NSubstitute mocks because
    // NSubstitute cannot proxy IValidator<T> when T is a private nested type and FluentValidation
    // is strong-named — DynamicProxy requires the type argument to be accessible.

    [Fact]
    public async Task Handle_WhenNoValidatorsRegistered_PassesThrough()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<NoValidatorRequest, string>([]);

        var result = await behavior.Handle(new NoValidatorRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenValidationPasses_CallsNext()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<ValidRequest, string>([new ValidRequest.Validator()]);

        var result = await behavior.Handle(new ValidRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenValidationFails_ReturnsFailureForResultResponse()
    {
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(Success("result")); };
        var behavior = new ValidationBehavior<InvalidRequest, Result<string>>([new InvalidRequest.Validator()]);

        var result = await behavior.Handle(new InvalidRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenValidationFails_ThrowsValidationExceptionForDirectResponse()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var behavior = new ValidationBehavior<InvalidRequest, string>([new InvalidRequest.Validator()]);

        await Should.ThrowAsync<ValidationException>(
            () => behavior.Handle(new InvalidRequest(), next, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenMultipleValidatorsFail_RunsAllAndCollectsErrors()
    {
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(Success("result")); };

        // Two separate validators both fail — behavior must run both (not fail-fast).
        var behavior = new ValidationBehavior<InvalidRequest, Result<string>>([
            new InvalidRequest.Validator("F1", "error-1"),
            new InvalidRequest.Validator("F2", "error-2")
        ]);

        var result = await behavior.Handle(new InvalidRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenAllValidatorsPass_CallsNext()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var behavior = new ValidationBehavior<ValidRequest, string>([
            new ValidRequest.Validator(),
            new ValidRequest.Validator()
        ]);

        var result = await behavior.Handle(new ValidRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenValidationFails_ErrorIsValidationError()
    {
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("result"));
        var behavior = new ValidationBehavior<InvalidRequest, Result<string>>([new InvalidRequest.Validator()]);

        var result = await behavior.Handle(new InvalidRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<MediatR.Behaviors.Errors.ValidationError>();
    }

    [Fact]
    public async Task Handle_WhenValidationFails_ErrorContainsAllCollectedFailures()
    {
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("result"));
        var behavior = new ValidationBehavior<InvalidRequest, Result<string>>([
            new InvalidRequest.Validator("F1", "error-1"),
            new InvalidRequest.Validator("F2", "error-2")
        ]);

        var result = await behavior.Handle(new InvalidRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<MediatR.Behaviors.Errors.ValidationError>();
        error.Failures.Count.ShouldBeGreaterThanOrEqualTo(2);
        error.Failures.ShouldContain(f => f.PropertyName == "F1");
        error.Failures.ShouldContain(f => f.PropertyName == "F2");
    }

    [Fact]
    public async Task Handle_WhenCancellationRequested_CancellationTokenForwardedToValidators()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var validator = new CapturedCancellationValidator();
        var behavior = new ValidationBehavior<CancelledRequest, string>([validator]);

        await behavior.Handle(new CancelledRequest(), () => Task.FromResult("result"), cts.Token);

        validator.ReceivedToken.IsCancellationRequested.ShouldBeTrue();
    }

    private sealed record NoValidatorRequest;

    private sealed record ValidRequest
    {
        internal sealed class Validator : AbstractValidator<ValidRequest>
        {
            // No rules — always passes.
        }
    }

    private sealed record InvalidRequest
    {
        internal sealed class Validator : AbstractValidator<InvalidRequest>
        {
            internal Validator(string field = "Field", string message = "is required")
            {
                RuleFor(x => x).Must(_ => false).WithName(field).WithMessage(message);
            }
        }
    }

    [Fact]
    public async Task Handle_WhenOneValidatorPassesOneValidatorFails_CollectsOnlyFailingErrors()
    {
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(Success("result")); };
        var behavior = new ValidationBehavior<InvalidRequest, Result<string>>([
            new AlwaysPassingInvalidRequestValidator(),
            new InvalidRequest.Validator("MustFailField", "always fails")
        ]);

        var result = await behavior.Handle(new InvalidRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<MediatR.Behaviors.Errors.ValidationError>();
        error.Failures.Count.ShouldBe(1);
        error.Failures[0].PropertyName.ShouldBe("MustFailField");
        callCount.ShouldBe(0);
    }

    private sealed record CancelledRequest;

    private sealed class AlwaysPassingInvalidRequestValidator : AbstractValidator<InvalidRequest>
    {
        // No rules — always passes. Used to test partial-failure collection.
    }

    private sealed class CapturedCancellationValidator : AbstractValidator<CancelledRequest>
    {
        public CancellationToken ReceivedToken { get; private set; }

        public override Task<FluentValidation.Results.ValidationResult> ValidateAsync(
            ValidationContext<CancelledRequest> context,
            CancellationToken cancellation = default)
        {
            ReceivedToken = cancellation;
            return Task.FromResult(new FluentValidation.Results.ValidationResult());
        }
    }
}
