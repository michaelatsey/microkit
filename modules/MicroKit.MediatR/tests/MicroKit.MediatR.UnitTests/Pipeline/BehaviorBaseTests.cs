using MediatR;
using MicroKit.MediatR;
using MicroKit.Result;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Pipeline;

/// <summary>
/// Verifies the protected-static helpers on <see cref="BehaviorBase{TRequest,TResponse}"/>
/// that are shared by all six built-in behaviors:
/// <list type="bullet">
///   <item><see cref="BehaviorBase{TRequest,TResponse}.IsResultResponse"/> — cached per closed generic</item>
///   <item><see cref="BehaviorBase{TRequest,TResponse}.CreateFailureOrThrow"/> — returns failure or throws based on TResponse</item>
/// </list>
/// A concrete subclass exposes these via public static wrappers because they are
/// protected, not testable from outside the inheritance hierarchy otherwise.
/// </summary>
public sealed class BehaviorBaseTests
{
    [Fact]
    public void IsResultResponse_WhenTResponseIsResultT_ReturnsTrue()
        => TestBehavior<BaseRequest, Result<string>>.ExposedIsResultResponse.ShouldBeTrue();

    [Fact]
    public void IsResultResponse_WhenTResponseIsDirectString_ReturnsFalse()
        => TestBehavior<BaseRequest, string>.ExposedIsResultResponse.ShouldBeFalse();

    [Fact]
    public void IsResultResponse_WhenTResponseIsResultUnit_ReturnsTrue()
        => TestBehavior<BaseRequest, Result<MicroKit.Result.Unit>>.ExposedIsResultResponse.ShouldBeTrue();

    [Fact]
    public void CreateFailureOrThrow_WhenResultTResponse_ReturnsFailureResult()
    {
        var error = new TestError();
        var fallback = new InvalidOperationException("should not be thrown");

        var result = TestBehavior<BaseRequest, Result<string>>.ExposedCreateFailure(error, fallback);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void CreateFailureOrThrow_WhenDirectResponse_ThrowsFallbackException()
    {
        var error = new TestError();
        var fallback = new InvalidOperationException("expected fallback");

        var ex = Should.Throw<InvalidOperationException>(
            () => TestBehavior<BaseRequest, string>.ExposedCreateFailure(error, fallback));

        ex.ShouldBe(fallback);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private sealed record BaseRequest;

    private sealed record TestError() : Error(ErrorCode.From("TEST.BASE"), "base test error");

    /// <summary>
    /// Concrete subclass of BehaviorBase that exposes the protected static
    /// members for direct testing without routing through a full behavior invocation.
    /// </summary>
    private sealed class TestBehavior<TRequest, TResponse> : BehaviorBase<TRequest, TResponse>
        where TRequest : notnull
    {
        public override int Order => 999;

        public override Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next();

        // Expose protected static members for assertions.
        public static bool ExposedIsResultResponse => IsResultResponse;

        public static TResponse ExposedCreateFailure(IError error, Exception fallback)
            => CreateFailureOrThrow(error, fallback);
    }
}
