using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

public sealed class ResultFactoryTests
{
    [Fact]
    public void Success_WhenCalled_ReturnsSuccess()
    {
        // Act
        Result result = ResultFactory.Success();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void SuccessOfT_WhenCalled_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = ResultFactory.Success(42);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_WhenCalled_ReturnsFailureWithError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = ResultFactory.Failure(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void FailureOfT_WhenCalled_ReturnsFailureWithError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result<int> result = ResultFactory.Failure<int>(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Should.Throw<ArgumentNullException>(() => ResultFactory.Failure(null!));
        Should.Throw<ArgumentNullException>(() => ResultFactory.Failure<int>(null!));
    }

    [Fact]
    public void Try_WhenActionThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result result = ResultFactory.Try(() => throw new InvalidOperationException("boom"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public void Try_WhenActionSucceeds_ReturnsSuccess()
    {
        // Act
        Result result = ResultFactory.Try(Result.Success);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Try_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result result = ResultFactory.Try(() => throw new InvalidOperationException("boom"), _ => mapped);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
    }

    [Fact]
    public void TryOfT_WhenFuncReturns_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = ResultFactory.Try(() => 42);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void TryOfT_WhenFuncThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = ResultFactory.Try<int>(() => throw new InvalidOperationException("boom"));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public void TryOfT_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result<int> result = ResultFactory.Try<int>(() => throw new InvalidOperationException("boom"), _ => mapped);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
    }

    [Fact]
    public void Try_WhenDelegateIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Should.Throw<ArgumentNullException>(() => ResultFactory.Try((Func<Result>)null!));
        Should.Throw<ArgumentNullException>(() => ResultFactory.Try<int>(null!));
        Should.Throw<ArgumentNullException>(() => ResultFactory.Try(Result.Success, null!));
        Should.Throw<ArgumentNullException>(() => ResultFactory.Try(() => 42, null!));
    }

    [Fact]
    public async Task TryAsync_WhenActionSucceeds_ReturnsSuccess()
    {
        // Act
        Result result = await ResultFactory.TryAsync(async () =>
        {
            await Task.Yield();
            return Result.Success();
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAsync_WhenActionThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result result = await ResultFactory.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result result = await ResultFactory.TryAsync(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            _ => mapped);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
    }

    [Fact]
    public async Task TryAsyncOfT_WhenFuncReturns_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = await ResultFactory.TryAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task TryAsyncOfT_WhenFuncThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = await ResultFactory.TryAsync<int>(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public async Task TryAsyncOfT_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result<int> result = await ResultFactory.TryAsync<int>(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            _ => mapped);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
    }

    [Fact]
    public async Task TryAsync_WhenDelegateIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await ResultFactory.TryAsync((Func<Task<Result>>)null!));
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await ResultFactory.TryAsync<int>(null!));
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await ResultFactory.TryAsync(() => Task.FromResult(Result.Success()), null!));
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await ResultFactory.TryAsync(() => Task.FromResult(42), null!));
    }

    private const string SecretMessage = "secret connection string";

    [Fact(Skip = "#184 phase 2")]
    public void Try_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        Should.Throw<OperationCanceledException>(() => ResultFactory.Try(() => throw new OperationCanceledException()));
        Should.Throw<OperationCanceledException>(() => ResultFactory.Try<int>(() => throw new OperationCanceledException()));
    }

    [Fact(Skip = "#184 phase 2")]
    public async Task TryAsync_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ResultFactory.TryAsync(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ResultFactory.TryAsync<int>(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
    }

    [Fact(Skip = "#184 phase 2")]
    public void Try_WithoutMapper_WhenDelegateThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = ResultFactory.Try(() => throw new InvalidOperationException(SecretMessage));
        Result<int> typed = ResultFactory.Try<int>(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.Exception);
        result.Error.Message.ShouldNotContain(SecretMessage);
        typed.IsFailure.ShouldBeTrue();
        typed.Error.Code.ShouldBe(ErrorCode.Exception);
        typed.Error.Message.ShouldNotContain(SecretMessage);
    }

    [Fact(Skip = "#184 phase 2")]
    public async Task TryAsync_WithoutMapper_WhenDelegateThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = await ResultFactory.TryAsync(() => Task.FromException<Result>(new InvalidOperationException(SecretMessage)));
        Result<int> typed = await ResultFactory.TryAsync(() => Task.FromException<int>(new InvalidOperationException(SecretMessage)));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.Exception);
        result.Error.Message.ShouldNotContain(SecretMessage);
        typed.IsFailure.ShouldBeTrue();
        typed.Error.Code.ShouldBe(ErrorCode.Exception);
        typed.Error.Message.ShouldNotContain(SecretMessage);
    }
}
