using MediatR;
using MicroKit.Logging;
using MicroKit.MediatR.Behaviors;
using MicroKit.Result;
using static MicroKit.Result.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class LoggingBehaviorTests
{
    // NullLogger<T> avoids NSubstitute proxy issues with strong-named ILogger from
    // Microsoft.Extensions.Logging.Abstractions when T has a private nested type argument.
    // We do not verify log calls (LoggingBehavior uses [LoggerMessage] static gen methods
    // which are not interceptable via NSubstitute), so NullLogger is the correct choice.

    [Fact]
    public async Task Handle_WhenRequestSucceeds_CallsNextOnce()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(
            NullLogger<LoggingBehavior<SimpleLogRequest, string>>.Instance);

        var result = await behavior.Handle(new SimpleLogRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenNextThrows_PropagatesExceptionUnmodified()
    {
        var expected = new InvalidOperationException("handler exploded");
        RequestHandlerDelegate<string> next = () => Task.FromException<string>(expected);
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(
            NullLogger<LoggingBehavior<SimpleLogRequest, string>>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new SimpleLogRequest(), next, CancellationToken.None));

        ex.ShouldBe(expected);
    }

    [Fact]
    public async Task Handle_WhenRequestProcessed_ReturnsHandlerResponseUnmodified()
    {
        const string expected = "untouched-response";
        RequestHandlerDelegate<string> next = () => Task.FromResult(expected);
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(
            NullLogger<LoggingBehavior<SimpleLogRequest, string>>.Instance);

        var result = await behavior.Handle(new SimpleLogRequest(), next, CancellationToken.None);

        result.ShouldBe(expected);
    }

    [Fact]
    public async Task Handle_WhenNextThrows_DoesNotSwallowException()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            callCount++;
            return Task.FromException<string>(new TimeoutException());
        };
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(
            NullLogger<LoggingBehavior<SimpleLogRequest, string>>.Instance);

        await Should.ThrowAsync<TimeoutException>(
            () => behavior.Handle(new SimpleLogRequest(), next, CancellationToken.None));

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenRequestDispatched_BeginsScope_WithCommandNameAndOperationId()
    {
        var logger = new CapturingLogger();
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(logger);

        await behavior.Handle(new SimpleLogRequest(), () => Task.FromResult("result"), CancellationToken.None);

        logger.Scopes.Count.ShouldBe(1);
        var scope = logger.Scopes[0].ShouldBeOfType<Dictionary<string, object?>>();
        scope.ShouldContainKey(LogPropertyNames.CommandName);
        scope[LogPropertyNames.CommandName].ShouldBe(nameof(SimpleLogRequest));
        scope.ShouldContainKey(LogPropertyNames.OperationId);
        scope[LogPropertyNames.OperationId].ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenRequestDispatched_ScopeContainsExactlyCommandNameAndOperationId()
    {
        // Verify that LoggingBehavior does not add any keys beyond the two canonical
        // LogPropertyNames.CommandName and LogPropertyNames.OperationId — no accidental noise.
        var logger = new CapturingLogger();
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(logger);

        await behavior.Handle(new SimpleLogRequest(), () => Task.FromResult("result"), CancellationToken.None);

        var scope = logger.Scopes[0].ShouldBeOfType<Dictionary<string, object?>>();
        scope.Count.ShouldBe(2, "scope must contain exactly CommandName and OperationId — no extra keys");
        scope.Keys.ShouldContain(LogPropertyNames.CommandName);
        scope.Keys.ShouldContain(LogPropertyNames.OperationId);
    }

    [Fact]
    public async Task Handle_WhenRequestDispatched_OperationIdIsNonEmptyString()
    {
        // OperationId is sourced from Activity.Current?.Id or a new Guid — must always be non-empty.
        var logger = new CapturingLogger();
        var behavior = new LoggingBehavior<SimpleLogRequest, string>(logger);

        await behavior.Handle(new SimpleLogRequest(), () => Task.FromResult("result"), CancellationToken.None);

        var scope = logger.Scopes[0].ShouldBeOfType<Dictionary<string, object?>>();
        var operationId = scope[LogPropertyNames.OperationId].ShouldBeOfType<string>();
        operationId.ShouldNotBeNullOrEmpty("OperationId must be a non-empty string derived from Activity or a new Guid");
    }

    [Fact]
    public async Task Handle_WhenResultFailureReturned_ReturnsFailureUnmodified()
    {
        // Fix #2 (MEDIUM): the new business-failure branch in LoggingBehavior must not
        // modify or swallow a Result.Failure response — it must pass it through untouched.
        var failure = Failure<string>(new LoggingTestError());
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(failure);
        var behavior = new LoggingBehavior<SimpleLogRequest, Result<string>>(
            NullLogger<LoggingBehavior<SimpleLogRequest, Result<string>>>.Instance);

        var result = await behavior.Handle(new SimpleLogRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.ShouldBe(failure);
    }

    private sealed record LoggingTestError() : Error(ErrorCode.From("TEST.LOGGING"), "logging test error");

    private sealed record SimpleLogRequest;

    private sealed class CapturingLogger : ILogger<LoggingBehavior<SimpleLogRequest, string>>
    {
        public readonly List<object?> Scopes = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
