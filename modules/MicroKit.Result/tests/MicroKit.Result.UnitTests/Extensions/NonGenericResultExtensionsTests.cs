using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Extensions;

// ADR-RESULT-001 D9: the non-generic Result has the sync operations Result<T> has, and Bind goes from a value to a
// command. A default Result throws ResultException where Result<T> does (Map, Bind, Ensure); MapError and Compensate
// return it unchanged, as on Result<T>.
public sealed class NonGenericResultExtensionsTests
{
    #region Map

    [Fact]
    public void Map_WhenSuccess_ReturnsSuccessWithMapperValue()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<int> mapped = result.Map(() => 42);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(42);
    }

    [Fact]
    public void Map_WhenFailure_PropagatesSameErrorWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var mapped = result.Map(() =>
        {
            called = true;
            return 42;
        });

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public void Map_WhenDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Map(() =>
        {
            called = true;
            return 42;
        }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Map_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Map<int>(null!));
        thrown.ParamName.ShouldBe("mapper");
    }

    [Fact]
    public void Map_WhenFailureAndMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Map<int>(null!));
        thrown.ParamName.ShouldBe("mapper");
    }

    [Fact]
    public void Map_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => result.Map<string>(() => null!));
    }

    [Fact]
    public void Map_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Map<int>(() => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Bind (Result -> Result)

    [Fact]
    public void Bind_WhenSuccess_ReturnsBinderResult()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result bound = result.Bind(() => Result.Failure(error));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Bind_WhenSuccessAndBinderSucceeds_ReturnsSuccess()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result bound = result.Bind(() =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        called.ShouldBeTrue();
    }

    [Fact]
    public void Bind_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result bound = result.Bind(() =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public void Bind_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Bind(() =>
        {
            called = true;
            return Result.Success();
        }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Bind_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Bind((Func<Result>)null!));
        thrown.ParamName.ShouldBe("binder");
    }

    #endregion

    #region Bind (Result -> Result<TOut>)

    [Fact]
    public void BindToValue_WhenSuccess_ReturnsBinderResult()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<string> bound = result.Bind(() => Result.Success("created"));

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe("created");
    }

    [Fact]
    public void BindToValue_WhenSuccessAndBinderFails_ReturnsBinderFailure()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result<string> bound = result.Bind(() => Result.Failure<string>(error));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void BindToValue_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        Result<string> bound = result.Bind(() =>
        {
            called = true;
            return Result.Success("created");
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public void BindToValue_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Bind(() =>
        {
            called = true;
            return Result.Success(1);
        }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void BindToValue_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Bind((Func<Result<int>>)null!));
        thrown.ParamName.ShouldBe("binder");
    }

    #endregion

    #region Bind (Result<TIn> -> Result)

    [Fact]
    public void BindToCommand_WhenSuccess_PassesValueAndReturnsBinderResult()
    {
        // Arrange
        var seen = 0;
        var result = Result.Success(21);

        // Act
        Result bound = result.Bind(x =>
        {
            seen = x;
            return Result.Success();
        });

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        seen.ShouldBe(21);
    }

    [Fact]
    public void BindToCommand_WhenSuccessAndBinderFails_ReturnsBinderFailure()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        Result bound = result.Bind(_ => Result.Failure(error));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void BindToCommand_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result bound = result.Bind(_ =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public void BindToCommand_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Bind(_ =>
        {
            called = true;
            return Result.Success();
        }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void BindToCommand_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Bind((Func<int, Result>)null!));
        thrown.ParamName.ShouldBe("binder");
    }

    #endregion

    #region Ensure

    [Fact]
    public void Ensure_WhenSuccessAndPredicateTrue_ReturnsSuccess()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result ensured = result.Ensure(() => true, new TestError());

        // Assert
        ensured.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Ensure_WhenSuccessAndPredicateFalse_ReturnsFailureWithError()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success();

        // Act
        Result ensured = result.Ensure(() => false, error);

        // Assert
        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Ensure_WhenFailure_PropagatesOriginalErrorWithoutCallingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure(original);

        // Act
        Result ensured = result.Ensure(() =>
        {
            called = true;
            return false;
        }, new OtherError());

        // Assert
        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBeSameAs(original);
        called.ShouldBeFalse();
    }

    [Fact]
    public void Ensure_WhenDefault_ThrowsResultExceptionWithoutCallingPredicate()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Ensure(() =>
        {
            called = true;
            return true;
        }, new TestError()));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Ensure_WhenPredicateIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Ensure(null!, new TestError()));
        thrown.ParamName.ShouldBe("predicate");
    }

    [Fact]
    public void Ensure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Ensure(() => true, null!));
        thrown.ParamName.ShouldBe("error");
    }

    #endregion

    #region MapError

    [Fact]
    public void MapError_WhenFailure_ReturnsFailureWithMappedError()
    {
        // Arrange
        var original = new TestError();
        var replacement = new OtherError();
        IError? seen = null;
        var result = Result.Failure(original);

        // Act
        Result mapped = result.MapError(e =>
        {
            seen = e;
            return replacement;
        });

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(replacement);
        seen.ShouldBeSameAs(original);
    }

    [Fact]
    public void MapError_WhenSuccess_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result mapped = result.MapError(e =>
        {
            called = true;
            return e;
        });

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        called.ShouldBeFalse();
    }

    [Fact]
    public void MapError_WhenDefault_ReturnsDefaultWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        Result mapped = result.MapError(e =>
        {
            called = true;
            return e;
        });

        // Assert
        mapped.IsSuccess.ShouldBeFalse();
        mapped.IsFailure.ShouldBeFalse();
        mapped.ShouldBe(default(Result));
        called.ShouldBeFalse();
    }

    [Fact]
    public void MapError_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.MapError(_ => null!));
        thrown.ParamName.ShouldBe("error");
    }

    [Fact]
    public void MapError_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.MapError(null!));
        thrown.ParamName.ShouldBe("mapper");
    }

    #endregion

    #region Compensate

    [Fact]
    public void Compensate_WhenFailure_ReturnsFallbackResult()
    {
        // Arrange
        var original = new TestError();
        IError? seen = null;
        var result = Result.Failure(original);

        // Act
        Result compensated = result.Compensate(e =>
        {
            seen = e;
            return Result.Success();
        });

        // Assert
        compensated.IsSuccess.ShouldBeTrue();
        seen.ShouldBeSameAs(original);
    }

    [Fact]
    public void Compensate_WhenFailureAndFallbackFails_ReturnsFallbackFailure()
    {
        // Arrange
        var replacement = new OtherError();
        var result = Result.Failure(new TestError());

        // Act
        Result compensated = result.Compensate(_ => Result.Failure(replacement));

        // Assert
        compensated.IsFailure.ShouldBeTrue();
        compensated.Error.ShouldBeSameAs(replacement);
    }

    [Fact]
    public void Compensate_WhenSuccess_ReturnsSuccessWithoutCallingFallback()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        Result compensated = result.Compensate(_ =>
        {
            called = true;
            return Result.Failure(new TestError());
        });

        // Assert
        compensated.IsSuccess.ShouldBeTrue();
        called.ShouldBeFalse();
    }

    [Fact]
    public void Compensate_WhenDefault_ReturnsDefaultWithoutCallingFallback()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        Result compensated = result.Compensate(_ =>
        {
            called = true;
            return Result.Success();
        });

        // Assert
        compensated.IsSuccess.ShouldBeFalse();
        compensated.IsFailure.ShouldBeFalse();
        compensated.ShouldBe(default(Result));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Compensate_WhenFallbackIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Compensate(null!));
        thrown.ParamName.ShouldBe("fallback");
    }

    #endregion

    #region Chained

    [Fact]
    public void Chain_WhenEveryStepSucceeds_ReachesTheEnd()
    {
        // Arrange
        var result = Result.Success();

        // Act
        Result<string> chained = result
            .Ensure(() => true, new TestError())
            .Bind(() => Result.Success())
            .Bind(() => Result.Success(20))
            .Map(x => x + 1)
            .Bind(x => x > 0 ? Result.Success() : Result.Failure(new OtherError()))
            .Map(() => "done");

        // Assert
        chained.IsSuccess.ShouldBeTrue();
        chained.Value.ShouldBe("done");
    }

    [Fact]
    public void Chain_WhenAStepFails_SkipsLaterStepsAndKeepsTheError()
    {
        // Arrange
        var error = new OtherError();
        var laterCalls = 0;
        var result = Result.Success();

        // Act
        Result<string> chained = result
            .Ensure(() => false, error)
            .Bind(() =>
            {
                laterCalls++;
                return Result.Success();
            })
            .Map(() =>
            {
                laterCalls++;
                return "done";
            });

        // Assert
        chained.IsFailure.ShouldBeTrue();
        chained.Error.ShouldBeSameAs(error);
        laterCalls.ShouldBe(0);
    }

    [Fact]
    public void Chain_WhenFailureIsCompensated_ResumesOnSuccessRail()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act
        Result<int> chained = result
            .MapError(_ => new OtherError())
            .Compensate(e => e is OtherError ? Result.Success() : Result.Failure(e))
            .Map(() => 7);

        // Assert
        chained.IsSuccess.ShouldBeTrue();
        chained.Value.ShouldBe(7);
    }

    #endregion
}
