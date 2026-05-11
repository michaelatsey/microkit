using MicroKit.Domain.Primitives;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class ResultTests
{
    // --- Result (non-generic) ---

    [Fact]
    public void Success_IsSuccessTrue_IsFailureFalse()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsSuccessFalse_IsFailureTrue()
    {
        var error = Error.Failure("X", "msg");
        var result = Result.Failure(error);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Constructor_SuccessWithError_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => Result.Failure(Error.None));
    }

    // --- Result<T> ---

    [Fact]
    public void SuccessT_CarriesValue()
    {
        var result = Result.Success(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void FailureT_ValueAccessThrows()
    {
        var result = Result.Failure<int>(Error.Failure("X", "msg"));
        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ImplicitFromValue_CreatesSuccessResult()
    {
        Result<string> result = "hello";
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void ImplicitFromError_CreatesFailureResult()
    {
        var error = Error.NotFound("X", "Not found");
        Result<string> result = error;
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void SuccessT_WithNullableReferenceType_Works()
    {
        var result = Result.Success<string?>(null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Failure_CarriesCorrectErrorType()
    {
        var error = Error.Validation("Name.Required", "Name is required");
        var result = Result.Failure<string>(error);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
