using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

public sealed class ResultExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessageAndEmptyErrors()
    {
        // Act
        var exception = new ResultException("message");

        // Assert
        exception.Message.ShouldBe("message");
        exception.InnerException.ShouldBeNull();
        exception.Errors.ShouldNotBeNull();
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerExceptionAndEmptyErrors()
    {
        // Arrange
        var inner = new FormatException("inner");

        // Act
        var exception = new ResultException("message", inner);

        // Assert
        exception.Message.ShouldBe("message");
        exception.InnerException.ShouldBeSameAs(inner);
        exception.Errors.ShouldNotBeNull();
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithErrors_SetsErrors()
    {
        // Arrange
        var first = new TestError("a");
        var second = new OtherError();

        // Act
        var exception = new ResultException("message", [first, second]);

        // Assert
        exception.Message.ShouldBe("message");
        exception.InnerException.ShouldBeNull();
        exception.Errors.Count.ShouldBe(2);
        exception.Errors[0].ShouldBeSameAs(first);
        exception.Errors[1].ShouldBeSameAs(second);
    }

    [Fact]
    public void Constructor_WithNullErrors_SetsEmptyErrors()
    {
        // Act
        var exception = new ResultException("message", (IReadOnlyList<IError>)null!);

        // Assert
        exception.Errors.ShouldNotBeNull();
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithInnerExceptionAndErrors_SetsBoth()
    {
        // Arrange
        var inner = new FormatException("inner");
        var error = new TestError();

        // Act
        var exception = new ResultException("message", inner, [error]);

        // Assert
        exception.Message.ShouldBe("message");
        exception.InnerException.ShouldBeSameAs(inner);
        exception.Errors.ShouldHaveSingleItem().ShouldBeSameAs(error);
    }

    [Fact]
    public void Constructor_WithInnerExceptionAndNullErrors_SetsEmptyErrors()
    {
        // Act
        var exception = new ResultException("message", new FormatException("inner"), null!);

        // Assert
        exception.Errors.ShouldNotBeNull();
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void ResultException_WhenCreated_IsInvalidOperationException()
    {
        // Act
        var exception = new ResultException("message");

        // Assert
        exception.ShouldBeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void Value_WhenFailure_ThrowsWithErrorInMessageAndErrors()
    {
        // Arrange
        var error = new NotFoundTestError(7);
        Result<int> result = Result.Failure<int>(error);

        // Act
        var exception = Should.Throw<ResultException>(() => result.Value);

        // Assert
        exception.Message.ShouldContain("[TEST.NOT_FOUND]");
        exception.Message.ShouldContain("Item 7 not found");
        exception.Errors.ShouldHaveSingleItem().ShouldBeSameAs(error);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void Value_WhenDefault_ThrowsWithEmptyErrors()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var exception = Should.Throw<ResultException>(() => result.Value);

        // Assert
        exception.Message.ShouldBe(
            "Result was not initialized. Use Result.Success() or Result.Failure() factory methods.");
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Error_WhenSuccess_ThrowsWithEmptyErrors()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var exception = Should.Throw<ResultException>(() => result.Error);

        // Assert
        exception.Errors.ShouldBeEmpty();
    }
}
