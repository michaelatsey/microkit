using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

public sealed class ResultOfTTests
{
    private const string SecretMessage = "secret connection string";

    // ── Success ──────────────────────────────────────────────────────────

    [Fact]
    public void Success_WhenCreated_IsSuccessWithValue()
    {
        // Act
        Result<int> result = Result<int>.Success(42);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void ResultSuccessOfT_WhenCreated_IsSuccessWithValue()
    {
        // Act
        Result<string> result = Result.Success("hello");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void Success_WhenValueIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert — ADR-RESULT-001 D6: absence is a failure, not a null value
        Should.Throw<ArgumentNullException>(() => Result.Success<string>(null!)).ParamName.ShouldBe("value");
        Should.Throw<ArgumentNullException>(() => Result<int?>.Success(null));
        Should.Throw<ArgumentNullException>(() => { Result<string> converted = (string)null!; return converted; });
    }

    [Fact]
    public void Success_WhenValueIsDefaultOfValueType_IsSuccess()
    {
        // Act
        Result<int> result = Result.Success(0);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    [Fact]
    public void Error_WhenSuccess_ThrowsResultException()
    {
        // Arrange
        Result<int> result = Result.Success(42);

        // Act
        var exception = Should.Throw<ResultException>(() => result.Error);

        // Assert
        exception.Message.ShouldBe("Cannot access Error on a successful result.");
        exception.Errors.ShouldBeEmpty();
    }

    // ── Failure ──────────────────────────────────────────────────────────

    [Fact]
    public void Failure_WhenCreated_IsFailureWithError()
    {
        // Arrange
        var error = new TestError();

        // Act
        Result<int> result = Result<int>.Failure(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void ResultFailureOfT_WhenCreated_IsFailureWithError()
    {
        // Arrange
        var error = new NotFoundTestError(7);

        // Act
        Result<string> result = Result.Failure<string>(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(() => Result<int>.Failure(null!));

        // Assert
        exception.ParamName.ShouldBe("error");
    }

    [Fact]
    public void ResultFailureOfT_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Should.Throw<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [Fact]
    public void Value_WhenFailure_ThrowsResultExceptionCarryingError()
    {
        // Arrange
        var error = new TestError("x");
        Result<int> result = Result.Failure<int>(error);

        // Act
        var exception = Should.Throw<ResultException>(() => result.Value);

        // Assert
        exception.Message.ShouldBe("Cannot access Value on a failed result. Error: [TEST.ERROR] Test error: x");
        exception.Errors.ShouldHaveSingleItem().ShouldBeSameAs(error);
    }

    // ── default / new Result<T>() ────────────────────────────────────────

    [Fact]
    public void Default_WhenUninitialized_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        Result<int> result = default;

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_WhenParameterless_IsNeitherSuccessNorFailure()
    {
        // Arrange + Act
        var result = new Result<string>();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Value_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var exception = Should.Throw<ResultException>(() => result.Value);

        // Assert
        exception.Message.ShouldContain("not initialized");
        exception.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Error_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var exception = Should.Throw<ResultException>(() => result.Error);

        // Assert
        exception.Message.ShouldContain("not initialized");
    }

    [Fact]
    public void Value_WhenParameterlessConstructor_ThrowsResultException()
    {
        // Arrange
        var result = new Result<string>();

        // Act + Assert
        Should.Throw<ResultException>(() => result.Value);
        Should.Throw<ResultException>(() => result.Error);
    }

    // ── Implicit conversion ──────────────────────────────────────────────

    [Fact]
    public void ImplicitConversion_FromValue_ReturnsSuccess()
    {
        // Act
        Result<int> result = 42;

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void ImplicitConversion_FromReference_ReturnsSuccessWithSameInstance()
    {
        // Arrange
        var payload = new List<int> { 1, 2 };

        // Act
        Result<List<int>> result = payload;

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(payload);
    }

    [Fact]
    public void ImplicitConversion_WhenValueIsGenericResultAndTIsObject_ThrowsArgumentException()
    {
        // Arrange
        Result<int> failure = Result.Failure<int>(new TestError());

        // Act + Assert
        Should.Throw<ArgumentException>(() => ToObjectResult(failure));
    }

    [Fact]
    public void ImplicitConversion_WhenValueIsResultAndTIsObject_ThrowsArgumentException()
    {
        // Arrange
        Result failure = Result.Failure(new TestError());

        // Act + Assert
        Should.Throw<ArgumentException>(() => ToObjectResult(failure));
    }

    // ── Try (no mapper) ──────────────────────────────────────────────────

    [Fact]
    public void Try_WhenFuncReturns_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = Result<int>.Try(() => 42);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Try_WhenFuncThrows_ErrorMetadataDoesNotExposeExceptionType()
    {
        // Act
        Result<int> result = Result<int>.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert: internal details go to the log, not to Metadata (security guarantee)
        result.IsFailure.ShouldBeTrue();
        result.Error.Metadata.ContainsKey("exceptionType").ShouldBeFalse();
    }

    [Fact]
    public void Try_WhenFuncThrows_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = Result<int>.Try(() => throw new InvalidOperationException(SecretMessage));

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
        result.Error.Severity.ShouldBe(ErrorSeverity.Error);
    }

    [Fact]
    public void Try_WhenFuncThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result<int> result = Result<int>.Try(() => throw new InvalidOperationException(SecretMessage));

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
            () => Result<int>.Try(() => throw new OperationCanceledException()));
    }

    [Fact]
    public void Try_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(() => Result<int>.Try(null!));

        // Assert
        exception.ParamName.ShouldBe("func");
    }

    // ── Try (with mapper) ────────────────────────────────────────────────

    [Fact]
    public void Try_WithMapper_WhenFuncReturns_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result<int> result = Result<int>.Try(() => 42, _ =>
        {
            called = true;
            return new TestError();
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        called.ShouldBeFalse();
    }

    [Fact]
    public void Try_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result<int> result = Result<int>.Try(() => throw thrown, ex =>
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
        Result<int> result = Result<int>.Try(() => throw new OperationCanceledException(), _ => mapped);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(mapped);
    }

    [Fact]
    public void Try_WithMapper_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        Should.Throw<ArgumentNullException>(
            () => Result<int>.Try(() => throw new InvalidOperationException("boom"), _ => null!));
    }

    [Fact]
    public void Try_WithMapper_WhenMapperThrows_PropagatesMapperException()
    {
        // Arrange
        var mapperException = new FormatException("mapper failed");

        // Act
        var exception = Should.Throw<FormatException>(
            () => Result<int>.Try(() => throw new InvalidOperationException("boom"), _ => throw mapperException));

        // Assert
        exception.ShouldBeSameAs(mapperException);
    }

    [Fact]
    public void Try_WithMapper_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(
            () => Result<int>.Try(null!, _ => new TestError()));

        // Assert
        exception.ParamName.ShouldBe("func");
    }

    [Fact]
    public void Try_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = Should.Throw<ArgumentNullException>(() => Result<int>.Try(() => 42, null!));

        // Assert
        exception.ParamName.ShouldBe("exceptionMapper");
    }

    // ── TryAsync (no mapper) ─────────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WhenFuncCompletesSynchronously_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(() => Task.FromResult(42));

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task TryAsync_WhenFuncCompletesAsynchronously_ReturnsSuccessWithValue()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task TryAsync_WhenFuncThrowsAfterAwait_ReturnsTechnicalFailure()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException(SecretMessage);
        });

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public async Task TryAsync_WhenFuncThrowsBeforeReturningTask_ReturnsTechnicalFailure()
    {
        // Arrange
        Func<Task<int>> func = () => throw new InvalidOperationException(SecretMessage);

        // Act
        Result<int> result = await Result<int>.TryAsync(func);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Category.ShouldBe(ErrorCategory.Technical);
    }

    [Fact]
    public async Task TryAsync_WhenFuncThrows_ErrorHasExceptionCodeAndGenericMessage()
    {
        // Act
        Result<int> result = await Result<int>.TryAsync(async () =>
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
            await Result<int>.TryAsync(async () =>
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
            await Result<int>.TryAsync(async () =>
            {
                await Task.Delay(1, cts.Token);
                return 42;
            }));
    }

    [Fact]
    public async Task TryAsync_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            async () => await Result<int>.TryAsync(null!));

        // Assert
        exception.ParamName.ShouldBe("func");
    }

    // ── TryAsync (with mapper) ───────────────────────────────────────────

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncReturns_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;

        // Act
        Result<int> result = await Result<int>.TryAsync(
            async () =>
            {
                await Task.Yield();
                return 42;
            },
            _ =>
            {
                called = true;
                return new TestError();
            });

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncThrows_ReturnsMappedError()
    {
        // Arrange
        var thrown = new InvalidOperationException("boom");
        Exception? received = null;
        var mapped = new OtherError();

        // Act
        Result<int> result = await Result<int>.TryAsync(
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
        Result<int> result = await Result<int>.TryAsync(
            async () =>
            {
                await Task.Delay(1, cts.Token);
                return 42;
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
            await Result<int>.TryAsync(
                async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                },
                _ => null!));
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            async () => await Result<int>.TryAsync(null!, _ => new TestError()));

        // Assert
        exception.ParamName.ShouldBe("func");
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            async () => await Result<int>.TryAsync(() => Task.FromResult(42), null!));

        // Assert
        exception.ParamName.ShouldBe("exceptionMapper");
    }

    // ── Equality ─────────────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenSuccessesWithEqualValues_ReturnsTrue()
    {
        // Arrange
        Result<string> left = Result.Success("value");
        Result<string> right = Result.Success(new string("value".AsSpan()));

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenSuccessesWithDifferentValues_ReturnsFalse()
    {
        // Arrange
        Result<int> left = Result.Success(1);
        Result<int> right = Result.Success(2);

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeFalse();
        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenFailuresWithEqualErrors_ReturnsTrue()
    {
        // Arrange
        Result<int> left = Result.Failure<int>(new TestError("same"));
        Result<int> right = Result.Failure<int>(new TestError("same"));

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
        Result<int> left = Result.Failure<int>(new TestError("a"));
        Result<int> right = Result.Failure<int>(new OtherError());

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
        Result<int> success = Result.Success(0);
        Result<int> failure = Result.Failure<int>(new TestError());

        // Act
        var equal = success.Equals(failure);

        // Assert
        equal.ShouldBeFalse();
        (success != failure).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenBothDefault_ReturnsTrue()
    {
        // Arrange
        Result<int> left = default;
        var right = new Result<int>();

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenDefaultAndSuccessOfDefaultValue_ReturnsFalse()
    {
        // Arrange
        Result<int> uninitialized = default;
        Result<int> success = Result.Success(0);

        // Act
        var equal = uninitialized.Equals(success);

        // Assert
        equal.ShouldBeFalse();
        (uninitialized != success).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenDefaultAndFailure_ReturnsFalse()
    {
        // Arrange
        Result<int> uninitialized = default;
        Result<int> failure = Result.Failure<int>(new TestError());

        // Act
        var equal = uninitialized.Equals(failure);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void EqualsObject_WhenBoxedEqualResult_ReturnsTrue()
    {
        // Arrange
        Result<int> result = Result.Success(5);
        object boxed = Result.Success(5);

        // Act
        var equal = result.Equals(boxed);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void EqualsObject_WhenNull_ReturnsFalse()
    {
        // Arrange
        Result<int> result = Result.Success(5);

        // Act
        var equal = result.Equals((object?)null); // a bare null binds to Equals(Result) through the conversion from Error (CS0037)

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void EqualsObject_WhenResultOfOtherType_ReturnsFalse()
    {
        // Arrange
        Result<int> result = Result.Success(5);
        object other = Result.Success(5L);

        // Act
        var equal = result.Equals(other);

        // Assert
        equal.ShouldBeFalse();
    }

    // ── ToString ─────────────────────────────────────────────────────────

    [Fact]
    public void ToString_WhenSuccess_DescribesValue()
    {
        // Arrange
        Result<int> result = Result.Success(42);

        // Act
        var text = result.ToString();

        // Assert
        text.ShouldBe("Result { Success: 42 }");
    }

    [Fact]
    public void ToString_WhenFailure_DescribesCodeAndMessage()
    {
        // Arrange
        Result<int> result = Result.Failure<int>(new TestError("x"));

        // Act
        var text = result.ToString();

        // Assert
        text.ShouldBe("Result { Failure: [TEST.ERROR] Test error: x }");
    }

    [Fact]
    public void ToString_WhenDefault_DescribesUninitialized()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var text = result.ToString();

        // Assert
        text.ShouldBe("Result { Uninitialized }");
    }

    // The implicit T -> Result<T> conversion with T = object accepts a result as the value: the
    // Phase 2 defect (`Result<object> r = Result.Failure<int>(error);` is a success today).
    private static Result<object> ToObjectResult(Result<int> value) => value;

    private static Result<object> ToObjectResult(Result value) => value;

    [Fact]
    public void Success_WhenValueIsAResult_ThrowsArgumentException()
    {
        // Arrange
        object failure = Result.Failure<int>(new TestError());

        // Act + Assert
        Should.Throw<ArgumentException>(() => Result<object>.Success(failure)).ParamName.ShouldBe("value");
        Should.Throw<ArgumentException>(() => Result.Success<object>(Result.Success()));
    }

    [Fact]
    public void ImplicitConversion_WhenValueIsAnOrdinaryObject_ReturnsSuccess()
    {
        // Act
        Result<object> result = (object)42;

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }
}
