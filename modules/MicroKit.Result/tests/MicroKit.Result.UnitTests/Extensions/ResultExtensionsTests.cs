using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Extensions;

public sealed class ResultExtensionsTests
{

    #region Map

    [Fact]
    public void Map_WhenSuccess_TransformsValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(84);
    }

    [Fact]
    public void Map_WhenSuccess_CanChangeValueType()
    {
        // Arrange
        var result = Result.Success(7);

        // Act
        var mapped = result.Map(x => $"#{x}");

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe("#7");
    }

    [Fact]
    public void Map_WhenFailure_PropagatesSameError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Map_WhenFailure_DoesNotInvokeMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Failure<int>(new TestError());

        // Act
        var mapped = result.Map(x =>
        {
            called = true;
            return x;
        });

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        called.ShouldBeFalse();
    }

    [Fact]
    public void Map_WhenDefault_ThrowsResultException()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Map(x =>
        {
            called = true;
            return x;
        }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Map_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Map<int, int>(null!));
        thrown.ParamName.ShouldBe("mapper");
    }

    [Fact]
    public void Map_WhenFailureAndMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Map<int, int>(null!));
        thrown.ParamName.ShouldBe("mapper");
    }

    [Fact]
    public void Map_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Map<int, int>(_ => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Bind

    [Fact]
    public void Bind_WhenSuccessAndBinderSucceeds_ReturnsBinderResult()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var bound = result.Bind(x => Result.Success(x * 2));

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe(42);
    }

    [Fact]
    public void Bind_WhenSuccessAndBinderFails_ReturnsBinderError()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        var bound = result.Bind(_ => Result.Failure<string>(error));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Bind_WhenFailure_PropagatesSameError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var bound = result.Bind(x => Result.Success(x * 2));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Bind_WhenFailure_DoesNotInvokeBinder()
    {
        // Arrange
        var called = false;
        var result = Result.Failure<int>(new TestError());

        // Act
        var bound = result.Bind(x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        called.ShouldBeFalse();
    }

    [Fact]
    public void Bind_WhenDefault_ThrowsResultException()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Bind(x =>
        {
            called = true;
            return Result.Success(x);
        }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Bind_WhenBinderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Bind<int, int>(null!));
        thrown.ParamName.ShouldBe("binder");
    }

    [Fact]
    public void Bind_WhenBinderThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Bind<int, int>(_ => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Match (generic)

    [Fact]
    public void Match_WhenSuccess_ReturnsOnSuccessValue()
    {
        // Arrange
        var failureCalled = false;
        var result = Result.Success(5);

        // Act
        var matched = result.Match(
            v => v * 10,
            _ =>
            {
                failureCalled = true;
                return -1;
            });

        // Assert
        matched.ShouldBe(50);
        failureCalled.ShouldBeFalse();
    }

    [Fact]
    public void Match_WhenFailure_PassesSameErrorToOnFailure()
    {
        // Arrange
        var error = new TestError();
        var successCalled = false;
        IError? received = null;
        var result = Result.Failure<int>(error);

        // Act
        var matched = result.Match(
            v =>
            {
                successCalled = true;
                return v;
            },
            e =>
            {
                received = e;
                return -1;
            });

        // Assert
        matched.ShouldBe(-1);
        received.ShouldBeSameAs(error);
        successCalled.ShouldBeFalse();
    }

    [Fact]
    public void Match_WhenDefault_ThrowsResultExceptionWithoutInvokingEitherBranch()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Match(
            v =>
            {
                called = true;
                return v;
            },
            _ =>
            {
                called = true;
                return -1;
            }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Match_WhenOnSuccessIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Match<int, int>(null!, _ => 0));
        thrown.ParamName.ShouldBe("onSuccess");
    }

    [Fact]
    public void Match_WhenOnFailureIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Match<int, int>(v => v, null!));
        thrown.ParamName.ShouldBe("onFailure");
    }

    [Fact]
    public void Match_WhenBranchThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Match<int, int>(v => v, _ => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Match (non-generic)

    [Fact]
    public void Match_NonGenericWhenSuccess_ReturnsOnSuccessValue()
    {
        // Arrange
        var failureCalled = false;
        var result = Result.Success();

        // Act
        var matched = result.Match(
            () => "ok",
            _ =>
            {
                failureCalled = true;
                return "ko";
            });

        // Assert
        matched.ShouldBe("ok");
        failureCalled.ShouldBeFalse();
    }

    [Fact]
    public void Match_NonGenericWhenFailure_PassesSameErrorToOnFailure()
    {
        // Arrange
        var error = new TestError();
        var successCalled = false;
        IError? received = null;
        var result = Result.Failure(error);

        // Act
        var matched = result.Match(
            () =>
            {
                successCalled = true;
                return "ok";
            },
            e =>
            {
                received = e;
                return "ko";
            });

        // Assert
        matched.ShouldBe("ko");
        received.ShouldBeSameAs(error);
        successCalled.ShouldBeFalse();
    }

    [Fact]
    public void Match_NonGenericWhenDefault_ThrowsResultExceptionWithoutInvokingEitherBranch()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Match(
            () =>
            {
                called = true;
                return 1;
            },
            _ =>
            {
                called = true;
                return -1;
            }));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Match_NonGenericWhenOnSuccessIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Match<int>(null!, _ => 0));
        thrown.ParamName.ShouldBe("onSuccess");
    }

    [Fact]
    public void Match_NonGenericWhenOnFailureIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Match(() => 0, null!));
        thrown.ParamName.ShouldBe("onFailure");
    }

    [Fact]
    public void Match_NonGenericWhenBranchThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Match<int>(() => throw boom, _ => 0));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Tap (generic)

    [Fact]
    public void Tap_WhenSuccess_InvokesActionWithValueAndReturnsSameResult()
    {
        // Arrange
        var seen = 0;
        var result = Result.Success(42);

        // Act
        var tapped = result.Tap(v => seen = v);

        // Assert
        seen.ShouldBe(42);
        tapped.ShouldBe(result);
        tapped.IsSuccess.ShouldBeTrue();
        tapped.Value.ShouldBe(42);
    }

    [Fact]
    public void Tap_WhenFailure_DoesNotInvokeActionAndPropagatesSameError()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = result.Tap(_ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Tap_WhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var tapped = result.Tap(_ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsSuccess.ShouldBeFalse();
        tapped.IsFailure.ShouldBeFalse();
        tapped.ShouldBe(default(Result<int>));
    }

    [Fact]
    public void Tap_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Tap((Action<int>)null!));
        thrown.ParamName.ShouldBe("action");
    }

    [Fact]
    public void Tap_WhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Tap(Throwing<int>(boom)));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task Tap_WithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success(42);

        // Act
        var tapped = await AsyncSettle.Settle(result.Tap(async _ =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        sideEffectRan.ShouldBeTrue();
        tapped.ShouldBe(result);
    }

    [Fact]
    public async Task Tap_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(42);

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Tap(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Tap (non-generic)

    [Fact]
    public void Tap_NonGenericWhenSuccess_InvokesActionAndReturnsSameResult()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        var tapped = result.Tap(() => called = true);

        // Assert
        called.ShouldBeTrue();
        tapped.ShouldBe(result);
        tapped.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Tap_NonGenericWhenFailure_DoesNotInvokeActionAndPropagatesSameError()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var tapped = result.Tap(() => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Tap_NonGenericWhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        var tapped = result.Tap(() => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsSuccess.ShouldBeFalse();
        tapped.IsFailure.ShouldBeFalse();
        tapped.ShouldBe(default(Result));
    }

    [Fact]
    public void Tap_NonGenericWhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Tap((Action)null!));
        thrown.ParamName.ShouldBe("action");
    }

    [Fact]
    public void Tap_NonGenericWhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Tap(Throwing(boom)));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task Tap_NonGenericWithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success();

        // Act
        var tapped = await AsyncSettle.Settle(result.Tap(async () =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        sideEffectRan.ShouldBeTrue();
        tapped.ShouldBe(result);
    }

    [Fact]
    public async Task Tap_NonGenericWithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Tap(async () =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region TapError (generic)

    [Fact]
    public void TapError_WhenFailure_InvokesActionWithSameErrorAndReturnsSameResult()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = result.TapError(e => seen = e);

        // Assert
        seen.ShouldBeSameAs(error);
        tapped.ShouldBe(result);
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void TapError_WhenSuccess_DoesNotInvokeActionAndKeepsValue()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsSuccess.ShouldBeTrue();
        tapped.Value.ShouldBe(42);
    }

    [Fact]
    public void TapError_WhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.ShouldBe(default(Result<int>));
    }

    [Fact]
    public void TapError_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.TapError((Action<IError>)null!));
        thrown.ParamName.ShouldBe("action");
    }

    [Fact]
    public void TapError_WhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.TapError(Throwing<IError>(boom)));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task TapError_WithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = await AsyncSettle.Settle(result.TapError(async e =>
        {
            await Task.Yield();
            seen = e;
        }));

        // Assert
        seen.ShouldBeSameAs(error);
        tapped.ShouldBe(result);
    }

    [Fact]
    public async Task TapError_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.TapError(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region TapError (non-generic)

    [Fact]
    public void TapError_NonGenericWhenFailure_InvokesActionWithSameErrorAndReturnsSameResult()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var tapped = result.TapError(e => seen = e);

        // Assert
        seen.ShouldBeSameAs(error);
        tapped.ShouldBe(result);
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void TapError_NonGenericWhenSuccess_DoesNotInvokeAction()
    {
        // Arrange
        var called = false;
        var result = Result.Success();

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void TapError_NonGenericWhenDefault_DoesNothingAndReturnsDefault()
    {
        // Arrange
        var called = false;
        Result result = default;

        // Act
        var tapped = result.TapError(_ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.ShouldBe(default(Result));
    }

    [Fact]
    public void TapError_NonGenericWhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.TapError((Action<IError>)null!));
        thrown.ParamName.ShouldBe("action");
    }

    [Fact]
    public void TapError_NonGenericWhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure(new TestError());

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.TapError(Throwing<IError>(boom)));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task TapError_NonGenericWithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var tapped = await AsyncSettle.Settle(result.TapError(async e =>
        {
            await Task.Yield();
            seen = e;
        }));

        // Assert
        seen.ShouldBeSameAs(error);
        tapped.ShouldBe(result);
    }

    [Fact]
    public async Task TapError_NonGenericWithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure(new TestError());

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.TapError(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Ensure

    [Fact]
    public void Ensure_WhenSuccessAndPredicateTrue_ReturnsSameResult()
    {
        // Arrange
        var result = Result.Success(10);

        // Act
        var ensured = result.Ensure(x => x > 5, new TestError());

        // Assert
        ensured.ShouldBe(result);
        ensured.IsSuccess.ShouldBeTrue();
        ensured.Value.ShouldBe(10);
    }

    [Fact]
    public void Ensure_WhenPredicateFalse_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = new TestError("too small");
        var result = Result.Success(1);

        // Act
        var ensured = result.Ensure(x => x > 5, error);

        // Assert
        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Ensure_WhenFailure_PropagatesOriginalErrorWithoutInvokingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        var ensured = result.Ensure(
            _ =>
            {
                called = true;
                return false;
            },
            new OtherError());

        // Assert
        called.ShouldBeFalse();
        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBeSameAs(original);
    }

    [Fact]
    public void Ensure_WhenDefault_ThrowsResultException()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Ensure(
            _ =>
            {
                called = true;
                return true;
            },
            new TestError()));
        called.ShouldBeFalse();
    }

    [Fact]
    public void Ensure_WhenPredicateIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Ensure(null!, new TestError()));
        thrown.ParamName.ShouldBe("predicate");
    }

    [Fact]
    public void Ensure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Ensure(_ => true, null!));
        thrown.ParamName.ShouldBe("error");
    }

    [Fact]
    public void Ensure_WhenPredicateThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Ensure(_ => throw boom, new TestError()));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region MapError

    [Fact]
    public void MapError_WhenFailure_ReturnsFailureWithMappedError()
    {
        // Arrange
        var original = new TestError();
        var replacement = new OtherError();
        IError? received = null;
        var result = Result.Failure<int>(original);

        // Act
        var mapped = result.MapError(e =>
        {
            received = e;
            return replacement;
        });

        // Assert
        received.ShouldBeSameAs(original);
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(replacement);
    }

    [Fact]
    public void MapError_WhenSuccess_ReturnsSameResultWithoutInvokingMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var mapped = result.MapError(e =>
        {
            called = true;
            return e;
        });

        // Assert
        called.ShouldBeFalse();
        mapped.ShouldBe(result);
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(42);
    }

    [Fact]
    public void MapError_WhenDefault_ReturnsDefaultWithoutInvokingMapper()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var mapped = result.MapError(e =>
        {
            called = true;
            return e;
        });

        // Assert
        called.ShouldBeFalse();
        mapped.IsSuccess.ShouldBeFalse();
        mapped.IsFailure.ShouldBeFalse();
        mapped.ShouldBe(default(Result<int>));
    }

    [Fact]
    public void MapError_WhenMapperIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.MapError(null!));
        thrown.ParamName.ShouldBe("mapper");
    }

    [Fact]
    public void MapError_WhenMapperReturnsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.MapError(_ => null!));
        thrown.ParamName.ShouldBe("error");
    }

    [Fact]
    public void MapError_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.MapError(_ => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Compensate

    [Fact]
    public void Compensate_WhenFailure_ReturnsFallbackSuccess()
    {
        // Arrange
        var original = new TestError();
        IError? received = null;
        var result = Result.Failure<int>(original);

        // Act
        var compensated = result.Compensate(e =>
        {
            received = e;
            return Result.Success(-1);
        });

        // Assert
        received.ShouldBeSameAs(original);
        compensated.IsSuccess.ShouldBeTrue();
        compensated.Value.ShouldBe(-1);
    }

    [Fact]
    public void Compensate_WhenFailureAndFallbackFails_ReturnsFallbackError()
    {
        // Arrange
        var fallbackError = new OtherError();
        var result = Result.Failure<int>(new TestError());

        // Act
        var compensated = result.Compensate(_ => Result.Failure<int>(fallbackError));

        // Assert
        compensated.IsFailure.ShouldBeTrue();
        compensated.Error.ShouldBeSameAs(fallbackError);
    }

    [Fact]
    public void Compensate_WhenSuccess_ReturnsSameResultWithoutInvokingFallback()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var compensated = result.Compensate(_ =>
        {
            called = true;
            return Result.Success(0);
        });

        // Assert
        called.ShouldBeFalse();
        compensated.ShouldBe(result);
        compensated.IsSuccess.ShouldBeTrue();
        compensated.Value.ShouldBe(42);
    }

    [Fact]
    public void Compensate_WhenDefault_ReturnsDefaultWithoutInvokingFallback()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var compensated = result.Compensate(_ =>
        {
            called = true;
            return Result.Success(0);
        });

        // Assert
        called.ShouldBeFalse();
        compensated.IsSuccess.ShouldBeFalse();
        compensated.IsFailure.ShouldBeFalse();
        compensated.ShouldBe(default(Result<int>));
    }

    [Fact]
    public void Compensate_WhenFallbackIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Compensate(null!));
        thrown.ParamName.ShouldBe("fallback");
    }

    [Fact]
    public void Compensate_WhenFallbackThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Compensate(_ => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region GetValueOrDefault

    [Fact]
    public void GetValueOrDefault_WhenSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrDefault(-1);

        // Assert
        value.ShouldBe(42);
    }

    [Fact]
    public void GetValueOrDefault_WhenFailure_ReturnsFallback()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act
        var value = result.GetValueOrDefault(-1);

        // Assert
        value.ShouldBe(-1);
    }

    [Fact]
    public void GetValueOrDefault_WhenDefault_ReturnsFallback()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var value = result.GetValueOrDefault(-1);

        // Assert
        value.ShouldBe(-1);
    }

    [Fact]
    public void GetValueOrDefault_WithoutArgumentWhenSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result.Success("value");

        // Act
        var value = result.GetValueOrDefault<string>();

        // Assert
        value.ShouldBe("value");
    }

    [Fact]
    public void GetValueOrDefault_WithoutArgumentWhenFailure_ReturnsDefaultOfT()
    {
        // Arrange
        var valueResult = Result.Failure<int>(new TestError());
        var referenceResult = Result.Failure<string>(new TestError());

        // Act
        var value = valueResult.GetValueOrDefault();
        string? reference = referenceResult.GetValueOrDefault<string>();

        // Assert
        value.ShouldBe(0);
        reference.ShouldBeNull();
    }

    [Fact]
    public void GetValueOrDefault_WithoutArgumentWhenDefault_ReturnsDefaultOfT()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var value = result.GetValueOrDefault();

        // Assert
        value.ShouldBe(0);
    }

    #endregion

    #region GetValueOrThrow

    [Fact]
    public void GetValueOrThrow_WhenSuccess_ReturnsValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrThrow();

        // Assert
        value.ShouldBe(42);
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryWhenSuccess_ReturnsValueWithoutInvokingFactory()
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var value = result.GetValueOrThrow(_ =>
        {
            called = true;
            return new InvalidOperationException();
        });

        // Assert
        value.ShouldBe(42);
        called.ShouldBeFalse();
    }

    [Fact]
    public void GetValueOrThrow_WhenFailure_ThrowsResultExceptionCarryingError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var thrown = Should.Throw<ResultException>(() => result.GetValueOrThrow());

        // Assert
        thrown.Errors.Count.ShouldBe(1);
        thrown.Errors[0].ShouldBeSameAs(error);
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryWhenFailure_ThrowsFactoryException()
    {
        // Arrange
        var error = new TestError();
        var custom = new InvalidOperationException("custom");
        IError? received = null;
        var result = Result.Failure<int>(error);

        // Act
        var thrown = Should.Throw<InvalidOperationException>(() => result.GetValueOrThrow(e =>
        {
            received = e;
            return custom;
        }));

        // Assert
        thrown.ShouldBeSameAs(custom);
        received.ShouldBeSameAs(error);
    }

    [Fact]
    public void GetValueOrThrow_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.GetValueOrThrow());
    }

    [Fact]
    public void GetValueOrThrow_WithFactoryWhenDefault_ThrowsResultExceptionWithoutInvokingFactory()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var thrown = Should.Throw<InvalidOperationException>(() => result.GetValueOrThrow(_ =>
        {
            called = true;
            return new InvalidOperationException("custom");
        }));

        // Assert
        thrown.ShouldBeOfType<ResultException>();
        called.ShouldBeFalse();
    }

    #endregion

    #region TryGetValue

    [Fact]
    public void TryGetValue_WhenSuccess_ReturnsTrueAndValue()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var found = result.TryGetValue(out var value);

        // Assert
        found.ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void TryGetValue_WhenFailure_ReturnsFalseAndDefault()
    {
        // Arrange
        var result = Result.Failure<string>(new TestError());

        // Act
        var found = result.TryGetValue(out var value);

        // Assert
        found.ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void TryGetValue_WhenDefault_ReturnsFalseAndDefault()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var found = result.TryGetValue(out var value);

        // Assert
        found.ShouldBeFalse();
        value.ShouldBe(0);
    }

    #endregion

    #region Finally (generic)

    [Fact]
    public void Finally_WhenSuccess_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result<int>? seen = null;
        var result = Result.Success(42);

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        seen.ShouldBe(result);
        finished.ShouldBe(result);
    }

    [Fact]
    public void Finally_WhenFailure_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result<int>? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        seen.ShouldBe(result);
        finished.IsFailure.ShouldBeTrue();
        finished.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Finally_WhenDefault_InvokesActionWithDefaultAndReturnsDefault()
    {
        // Arrange
        Result<int>? seen = null;
        Result<int> result = default;

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        seen.ShouldBe(default(Result<int>));
        finished.ShouldBe(default(Result<int>));
    }

    [Fact]
    public void Finally_WhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Finally((Action<Result<int>>)null!));
        thrown.ParamName.ShouldBe("action");
    }

    [Fact]
    public void Finally_WhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Finally(Throwing<Result<int>>(boom)));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task Finally_WithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success(42);

        // Act
        var finished = await AsyncSettle.Settle(result.Finally(async _ =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        sideEffectRan.ShouldBeTrue();
        finished.ShouldBe(result);
    }

    [Fact]
    public async Task Finally_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(42);

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Finally(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Finally (non-generic)

    [Fact]
    public void Finally_NonGenericWhenSuccess_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result? seen = null;
        var result = Result.Success();

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        seen.ShouldBe(result);
        finished.ShouldBe(result);
    }

    [Fact]
    public void Finally_NonGenericWhenFailure_InvokesActionWithResultAndReturnsSameResult()
    {
        // Arrange
        Result? seen = null;
        var error = new TestError();
        var result = Result.Failure(error);

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        seen.ShouldBe(result);
        finished.IsFailure.ShouldBeTrue();
        finished.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Finally_NonGenericWhenDefault_InvokesActionWithDefaultAndReturnsDefault()
    {
        // Arrange
        Result? seen = null;
        Result result = default;

        // Act
        var finished = result.Finally(r => seen = r);

        // Assert
        seen.ShouldBe(default(Result));
        finished.ShouldBe(default(Result));
    }

    [Fact]
    public void Finally_NonGenericWhenActionIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Finally((Action<Result>)null!));
        thrown.ParamName.ShouldBe("action");
    }

    [Fact]
    public void Finally_NonGenericWhenActionThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => result.Finally(Throwing<Result>(boom)));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task Finally_NonGenericWithAsyncLambda_AwaitsSideEffectBeforeReturning()
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success();

        // Act
        var finished = await AsyncSettle.Settle(result.Finally(async _ =>
        {
            await Task.Yield();
            sideEffectRan = true;
        }));

        // Assert
        sideEffectRan.ShouldBeTrue();
        finished.ShouldBe(result);
    }

    [Fact]
    public async Task Finally_NonGenericWithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success();

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await AsyncSettle.Settle(result.Finally(async _ =>
            {
                await Task.Yield();
                throw boom;
            })));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region ToResult

    [Fact]
    public void ToResult_WhenSuccess_ReturnsNonGenericSuccess()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var converted = result.ToResult();

        // Assert
        converted.IsSuccess.ShouldBeTrue();
        converted.ShouldBe(Result.Success());
    }

    [Fact]
    public void ToResult_WhenFailure_ReturnsNonGenericFailureWithSameError()
    {
        // Arrange
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var converted = result.ToResult();

        // Assert
        converted.IsFailure.ShouldBeTrue();
        converted.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void ToResult_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.ToResult());
    }

    #endregion

    #region Chained

    [Fact]
    public void Pipeline_WhenEveryStepSucceeds_FlowsOnSuccessRail()
    {
        // Arrange
        var tapped = new List<int>();
        var start = Result.Success(2);

        // Act
        var outcome = start
            .Map(x => x + 1)
            .Bind(x => Result.Success(x * 10))
            .Ensure(x => x > 10, new TestError("too small"))
            .Tap(tapped.Add)
            .Match(x => $"value {x}", e => e.Code.Value);

        // Assert
        outcome.ShouldBe("value 30");
        tapped.ShouldHaveSingleItem().ShouldBe(30);
    }

    [Fact]
    public void Pipeline_WhenStepFails_ShortCircuitsRemainingSteps()
    {
        // Arrange
        var error = new NotFoundTestError(7);
        var afterFailureCalls = 0;
        IError? tappedError = null;
        var start = Result.Success(7);

        // Act
        var outcome = start
            .Bind(id => Result.Failure<int>(new NotFoundTestError(id)))
            .Map(x =>
            {
                afterFailureCalls++;
                return x * 2;
            })
            .Ensure(
                _ =>
                {
                    afterFailureCalls++;
                    return true;
                },
                new TestError())
            .Tap(_ => afterFailureCalls++)
            .TapError(e => tappedError = e);

        // Assert
        afterFailureCalls.ShouldBe(0);
        outcome.IsFailure.ShouldBeTrue();
        outcome.Error.ShouldBe(error);
        tappedError.ShouldBeSameAs(outcome.Error);
    }

    [Fact]
    public void Pipeline_WhenFailureIsCompensated_ReturnsToSuccessRail()
    {
        // Arrange
        var start = Result.Failure<int>(new NotFoundTestError(1));

        // Act
        var outcome = start
            .MapError(e => new TestError(e.Message))
            .Compensate(_ => Result.Success(0))
            .Map(x => x + 1);

        // Assert
        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.ShouldBe(1);
    }

    #endregion

    // Typed as Action, so the call keeps binding to the Action overload once Phase 2 adds Func<..., Task> overloads.
    private static Action<T> Throwing<T>(Exception exception) => _ => throw exception;

    private static Action Throwing(Exception exception) => () => throw exception;
}
