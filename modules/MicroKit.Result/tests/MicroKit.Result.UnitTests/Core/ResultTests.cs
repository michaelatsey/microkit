using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

public sealed class ResultTests
{
    private const string SecretMessage = "secret connection string";

    // ── Success ──────────────────────────────────────────────────────────

    [Fact]
    public void Success_WhenCreated_IsSuccessAndNotFailure()
    {
        // Arrange + Act
        Result result = Result.Success();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Error_WhenSuccess_ThrowsResultException()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var exception = Should.Throw<ResultException>(() => result.Error);

        // Assert
        exception.Message.ShouldBe("Cannot access Error on a successful result.");
        exception.Errors.ShouldBeEmpty();
    }

    // ── Failure ──────────────────────────────────────────────────────────

    [Fact]
    public void Failure_WhenCreated_IsFailureAndNotSuccess()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Failure(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Error_WhenFailure_ReturnsSameError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Failure(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(() => Result.Failure(null!));

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    // ── default / new Result() ───────────────────────────────────────────

    [Fact]
    public void Default_WhenUninitialized_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        Result result = default;

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_WhenParameterless_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        var result = new Result();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Error_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result result = default;

        // Act
        var exception = Should.Throw<ResultException>(() => result.Error);

        // Assert
        exception.Message.ShouldContain("not initialized");
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Error_WhenParameterlessConstructor_ThrowsResultException()
    {
        // Arrange
        var result = new Result();

        // Act + Assert
        Should.Throw<ResultException>(() => result.Error);
    }

    // ── Try (no mapper) ──────────────────────────────────────────────────

    [Fact]
    public void Try_WhenActionReturnsSuccess_ReturnsSuccess()
    {
        // Act
        Result result = Result.Try(Result.Success);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Try_WhenActionReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Try(() => Result.Failure(error));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Try_WhenActionReturnsDefault_ReturnsUninitializedResult()
    {
        // Act
        Result result = Result.Try(() => default);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Try_WhenActionThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result result = Result.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
        result.Error.Severity.ShouldBe(ErrorSeverity.Error);
    }

    [Fact]
    public void Try_WhenActionThrows_ErrorMetadataDoesNotExposeExceptionType()
    {
        // Act
        Result result = Result.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert: internal details go to the log, not to Metadata (security guarantee)
        result.IsFailure.ShouldBeTrue();
        result.Error.Metadata.ContainsKey("exceptionType").ShouldBeFalse();
    }

    [Fact]
    public void Try_WhenActionThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = Result.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.Exception);
        result.Error.Message.ShouldNotContain(SecretMessage);
    }

    [Fact]
    public void Try_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        Should.Throw<OperationCanceledException>(
            () => Result.Try(() => throw new OperationCanceledException()));
    }

    [Fact]
    public void Try_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(() => Result.Try(null!));

        // Assert
        exception.ParamName.ShouldBe("action");
    }

    // ── Try (with mapper) ────────────────────────────────────────────────

    [Fact]
    public void Try_WithMapper_WhenActionReturnsSuccess_DoesNotCallMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result result = Result.Try(Result.Success, _ =>
        {
            called = true;
            return new TestError();
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        called.ShouldBeFalse();
    }

    [Fact]
    public void Try_WithMapper_WhenActionReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = Result.Try(() => Result.Failure(error), _ => new OtherError());

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Try_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result result = Result.Try(() => throw thrown, ex =>
        {
            received = ex;
            return mapped;
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
        received.ShouldBeSameAs(thrown);
    }

    [Fact]
    public void Try_WithMapper_WhenOperationCanceled_ReturnsMappedError()
    {
        // Arrange
        var mapped = new OtherError();

        // Act
        Result result = Result.Try(() => throw new OperationCanceledException(), _ => mapped);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
    }

    [Fact]
    public void Try_WithMapper_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Should.Throw<ArgumentNullException>(
            () => Result.Try(() => throw new InvalidOperationException("boom"), _ => null!));
    }

    [Fact]
    public void Try_WithMapper_WhenMapperThrows_PropagatesMapperException()
    {
        // Arrange
        var mapperException = new FormatException("mapper failed");

        // Act
        var exception = Should.Throw<FormatException>(
            () => Result.Try(() => throw new InvalidOperationException("boom"), _ => throw mapperException));

        // Assert
        exception.ShouldBeSameAs(mapperException);
    }

    [Fact]
    public void Try_WithMapper_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(
            () => Result.Try(null!, _ => new TestError()));

        // Assert
        exception.ParamName.ShouldBe("action");
    }

    [Fact]
    public void Try_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(() => Result.Try(Result.Success, null!));

        // Assert
        exception.ParamName.ShouldBe("exceptionMapper");
    }

    // ── TryAsync (no mapper) ─────────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WhenActionCompletesSynchronously_ReturnsSuccess()
    {
        // Act
        Result result = await Result.TryAsync(() => Task.FromResult(Result.Success()));

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAsync_WhenActionCompletesAsynchronously_ReturnsSuccess()
    {
        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            return Result.Success();
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TryAsync_WhenActionReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            return Result.Failure(error);
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrowsAfterAwait_ReturnsTechnicalFailure()
    {
        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException(SecretMessage);
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrowsBeforeReturningTask_ReturnsTechnicalFailure()
    {
        // Arrange
        Func<Task<Result>> action = () => throw new InvalidOperationException(SecretMessage);

        // Act
        Result result = await Result.TryAsync(action);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result result = await Result.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException(SecretMessage);
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.Exception);
        result.Error.Message.ShouldNotContain(SecretMessage);
    }

    [Fact]
    public async Task TryAsync_WithoutMapper_WhenOperationCanceled_RethrowsOperationCanceledException()
    {
        // Act + Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Result.TryAsync(async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            }));
    }

    [Fact]
    public async Task TryAsync_WithoutMapper_WhenCancelled_RethrowsTaskCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act + Assert
        await Should.ThrowAsync<TaskCanceledException>(async () =>
            await Result.TryAsync(async () =>
            {
                await Task.Delay(1, cts.Token);
                return Result.Success();
            }));
    }

    [Fact]
    public async Task TryAsync_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            async () => await Result.TryAsync(null!));

        // Assert
        exception.ParamName.ShouldBe("action");
    }

    // ── TryAsync (with mapper) ───────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionSucceeds_DoesNotCallMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result result = await Result.TryAsync(
            async () =>
            {
                await Task.Yield();
                return Result.Success();
            },
            _ =>
            {
                called = true;
                return new TestError();
            });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result result = await Result.TryAsync(
            async () =>
            {
                await Task.Yield();
                throw thrown;
            },
            ex =>
            {
                received = ex;
                return mapped;
            });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
        received.ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenCancelled_ReturnsMappedError()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result result = await Result.TryAsync(
            async () =>
            {
                await Task.Delay(1, cts.Token);
                return Result.Success();
            },
            ex =>
            {
                received = ex;
                return mapped;
            });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
        received.ShouldBeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await Result.TryAsync(
                async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                },
                _ => null!));
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            async () => await Result.TryAsync(null!, _ => new TestError()));

        // Assert
        exception.ParamName.ShouldBe("action");
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            async () => await Result.TryAsync(() => Task.FromResult(Result.Success()), null!));

        // Assert
        exception.ParamName.ShouldBe("exceptionMapper");
    }

    // ── Equality ─────────────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenBothSuccess_ReturnsTrue()
    {
        // Arrange
        Result left = Result.Success();
        Result right = Result.Success();

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenFailuresWithEqualErrors_ReturnsTrue()
    {
        // Arrange
        Result left = Result.Failure(new TestError("same"));
        Result right = Result.Failure(new TestError("same"));

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenFailuresWithDifferentErrors_ReturnsFalse()
    {
        // Arrange
        Result left = Result.Failure(new TestError("a"));
        Result right = Result.Failure(new TestError("b"));

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenSuccessAndFailure_ReturnsFalse()
    {
        // Arrange
        Result success = Result.Success();
        Result failure = Result.Failure(new TestError());

        // Act
        var equal = success.Equals(failure);

        // Assert
        equal.ShouldBeFalse();
        (success == failure).ShouldBeFalse();
        (success != failure).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenBothDefault_ReturnsTrue()
    {
        // Arrange
        Result left = default;
        var right = new Result();

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equals_WhenDefaultAndInitialized_ReturnsFalse(bool isSuccess)
    {
        // Arrange
        Result uninitialized = default;
        Result initialized = isSuccess ? Result.Success() : Result.Failure(new TestError());

        // Act
        var equal = uninitialized.Equals(initialized);

        // Assert
        equal.ShouldBeFalse();
        (uninitialized != initialized).ShouldBeTrue();
    }

    [Fact]
    public void EqualsObject_WhenBoxedEqualResult_ReturnsTrue()
    {
        // Arrange
        Result result = Result.Failure(new TestError("same"));
        object boxed = Result.Failure(new TestError("same"));

        // Act
        var equal = result.Equals(boxed);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void EqualsObject_WhenNull_ReturnsFalse()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var equal = result.Equals((object?)null); // a bare null binds to Equals(Result) through the conversion from Error (CS0037)

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void EqualsObject_WhenOtherType_ReturnsFalse()
    {
        // Arrange
        Result result = Result.Success();
        object other = Result.Success(Unit.Value);

        // Act
        var equal = result.Equals(other);

        // Assert
        equal.ShouldBeFalse();
    }

    // ── ToString ─────────────────────────────────────────────────────────

    [Fact]
    public void ToString_WhenSuccess_DescribesSuccess()
    {
        // Arrange
        Result result = Result.Success();

        // Act
        var text = result.ToString();

        // Assert
        text.ShouldBe("Result { Success }");
    }

    [Fact]
    public void ToString_WhenFailure_DescribesCodeAndMessage()
    {
        // Arrange
        Result result = Result.Failure(new TestError("x"));

        // Act
        var text = result.ToString();

        // Assert
        text.ShouldBe("Result { Failure: [TEST.ERROR] Test error: x }");
    }

    [Fact]
    public void ToString_WhenDefault_DescribesUninitialized()
    {
        // Arrange
        Result result = default;

        // Act
        var text = result.ToString();

        // Assert
        text.ShouldBe("Result { Uninitialized }");
    }
}
