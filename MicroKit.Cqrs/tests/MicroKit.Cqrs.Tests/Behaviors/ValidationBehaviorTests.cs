using FluentValidation;
using MediatR;
using MicroKit.Cqrs.MediatR.Behaviors;
using ValidationException = FluentValidation.ValidationException;

namespace MicroKit.Cqrs.Tests.Behaviors;

public sealed class ValidationBehaviorTests
{
    // ── No validators ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<ValidRequest, string>([]);

        var result = await behavior.Handle(new ValidRequest("x"), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
    }

    // ── All validators pass ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AllValidatorsPass_CallsNext()
    {
        var behavior = new ValidationBehavior<ValidRequest, string>(
        [
            new NonEmptyValidator(),
            new MaxLengthValidator()
        ]);

        var result = await behavior.Handle(new ValidRequest("hello"), _ => Task.FromResult("passed"), CancellationToken.None);

        Assert.Equal("passed", result);
    }

    // ── Fail-fast: first validator fails ───────────────────────────────────────

    [Fact]
    public async Task Handle_FirstValidatorFails_ThrowsWithoutCallingSecond()
    {
        var secondCalled = false;
        var failingFirst = new AlwaysFailValidator();
        var secondValidator = new CallTrackingValidator(() => secondCalled = true);
        var behavior = new ValidationBehavior<ValidRequest, string>([failingFirst, secondValidator]);
        var nextCalled = false;
        Task<string> Next(CancellationToken _) { nextCalled = true; return Task.FromResult("x"); }

        await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new ValidRequest("x"), Next, CancellationToken.None));

        Assert.False(secondCalled, "second validator must not run when first fails (fail-fast)");
        Assert.False(nextCalled, "next delegate must not be called after validation failure");
    }

    [Fact]
    public async Task Handle_FirstValidatorFails_ExceptionContainsErrors()
    {
        var behavior = new ValidationBehavior<ValidRequest, string>([new AlwaysFailValidator()]);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new ValidRequest(""), _ => Task.FromResult("x"), CancellationToken.None));

        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public async Task Handle_SecondValidatorFails_ThrowsWithoutCallingNext()
    {
        var behavior = new ValidationBehavior<ValidRequest, string>(
        [
            new NonEmptyValidator(),
            new AlwaysFailValidator()
        ]);
        var nextCalled = false;
        Task<string> Next(CancellationToken _) { nextCalled = true; return Task.FromResult("x"); }

        await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new ValidRequest("ok"), Next, CancellationToken.None));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Handle_FirstValidatorFails_SpecificErrorMessage()
    {
        var behavior = new ValidationBehavior<ValidRequest, string>([new NonEmptyValidator()]);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new ValidRequest(""), _ => Task.FromResult("x"), CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(ValidRequest.Value));
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PassesCancellationTokenToNext()
    {
        using var cts = new CancellationTokenSource();
        var behavior = new ValidationBehavior<ValidRequest, string>([]);
        CancellationToken captured = default;

        await behavior.Handle(new ValidRequest("x"), ct =>
        {
            captured = ct;
            return Task.FromResult("ok");
        }, cts.Token);

        Assert.Equal(cts.Token, captured);
    }

    // ── Concrete validators ───────────────────────────────────────────────────

    private sealed class NonEmptyValidator : AbstractValidator<ValidRequest>
    {
        public NonEmptyValidator() => RuleFor(x => x.Value).NotEmpty();
    }

    private sealed class MaxLengthValidator : AbstractValidator<ValidRequest>
    {
        public MaxLengthValidator() => RuleFor(x => x.Value).MaximumLength(100);
    }

    private sealed class AlwaysFailValidator : AbstractValidator<ValidRequest>
    {
        public AlwaysFailValidator() => RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails");
    }

    private sealed class CallTrackingValidator : AbstractValidator<ValidRequest>
    {
        public CallTrackingValidator(Action onValidate) => RuleFor(x => x.Value).Must(_ =>
        {
            onValidate();
            return true;
        });
    }
}

public sealed record ValidRequest(string Value) : IRequest<string>;
