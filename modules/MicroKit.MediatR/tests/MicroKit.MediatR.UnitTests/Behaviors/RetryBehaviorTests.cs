using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.MediatR.Behaviors.Pipeline;
using MicroKit.Result;
using static MicroKit.Result.Result;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class RetryBehaviorTests
{
    [Fact]
    public async Task Handle_WhenMarkerAbsent_PassesThrough()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var behavior = new RetryBehavior<NonRetryableRequest, string>();

        var result = await behavior.Handle(new NonRetryableRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenMarkerPresent_CallsNextOnce()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var behavior = new RetryBehavior<RetryableRequest1, string>();

        var result = await behavior.Handle(new RetryableRequest1(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsTransientException_Retries()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            callCount++;
            return callCount <= 2
                ? Task.FromException<string>(new IOException("transient"))
                : Task.FromResult("result");
        };
        var behavior = new RetryBehavior<RetryableRequest2, string>();

        var result = await behavior.Handle(new RetryableRequest2(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsOperationCanceled_DoesNotRetry()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            callCount++;
            return Task.FromException<string>(new OperationCanceledException());
        };
        var behavior = new RetryBehavior<RetryableRequest3, string>();

        await Should.ThrowAsync<OperationCanceledException>(
            () => behavior.Handle(new RetryableRequest3(), next, CancellationToken.None));

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsValidationException_DoesNotRetry()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            callCount++;
            return Task.FromException<string>(
                new ValidationException([new ValidationFailure("field", "error")]));
        };
        var behavior = new RetryBehavior<RetryableRequest4, string>();

        await Should.ThrowAsync<ValidationException>(
            () => behavior.Handle(new RetryableRequest4(), next, CancellationToken.None));

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsUnauthorizedAccessException_DoesNotRetry()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            callCount++;
            return Task.FromException<string>(new UnauthorizedAccessException());
        };
        var behavior = new RetryBehavior<RetryableRequest5, string>();

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => behavior.Handle(new RetryableRequest5(), next, CancellationToken.None));

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenMaxRetriesIsZero_ThrowsArgumentOutOfRangeException()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var behavior = new RetryBehavior<RetryableRequestZeroRetries, string>();

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => behavior.Handle(new RetryableRequestZeroRetries(), next, CancellationToken.None));

        ex.Message.ShouldContain("MaxRetries");
        ex.Message.ShouldContain("greater than zero");
    }

    [Fact]
    public async Task Handle_WhenMaxRetriesIsNegative_ThrowsArgumentOutOfRangeException()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var behavior = new RetryBehavior<RetryableRequestNegativeRetries, string>();

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => behavior.Handle(new RetryableRequestNegativeRetries(), next, CancellationToken.None));

        ex.Message.ShouldContain("MaxRetries");
        ex.Message.ShouldContain("greater than zero");
    }

    [Fact]
    public async Task Handle_WhenAllRetriesExhausted_ThrowsLastException()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            callCount++;
            return Task.FromException<string>(new IOException("always fails"));
        };
        var behavior = new RetryBehavior<ExhaustRetryRequest, string>();

        await Should.ThrowAsync<IOException>(
            () => behavior.Handle(new ExhaustRetryRequest(), next, CancellationToken.None));

        callCount.ShouldBe(3); // 1 initial + 2 retries
    }

    // Distinct TRequest types per test — RetryBehavior caches the Polly pipeline by TRequest type
    // in a process-wide ConcurrentDictionary. Reusing a type across tests that expect different
    // MaxRetries/Delay values would share the pipeline built from the first test that ran.

    private sealed record NonRetryableRequest;

    private sealed record RetryableRequest1 : IRetryableRequest
    {
        public int MaxRetries => 1;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableRequest2 : IRetryableRequest
    {
        public int MaxRetries => 3;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableRequest3 : IRetryableRequest
    {
        public int MaxRetries => 3;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableRequest4 : IRetryableRequest
    {
        public int MaxRetries => 3;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableRequest5 : IRetryableRequest
    {
        public int MaxRetries => 3;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableRequestZeroRetries : IRetryableRequest
    {
        public int MaxRetries => 0;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableRequestNegativeRetries : IRetryableRequest
    {
        public int MaxRetries => -1;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    [Fact]
    public async Task Handle_WhenResponseIsResultFailure_DoesNotRetry()
    {
        var callCount = 0;
        var failure = Failure<string>(new RetryTestError());
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(failure); };
        var behavior = new RetryBehavior<RetryableResultFailureRequest, Result<string>>();

        var result = await behavior.Handle(new RetryableResultFailureRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(1);
    }

    private sealed record ExhaustRetryRequest : IRetryableRequest
    {
        public int MaxRetries => 2;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryableResultFailureRequest : IRetryableRequest
    {
        public int MaxRetries => 3;
        public TimeSpan Delay => TimeSpan.FromMilliseconds(1);
    }

    private sealed record RetryTestError() : Error(ErrorCode.From("TEST.RETRY.ERROR"), "test retry error");
}
