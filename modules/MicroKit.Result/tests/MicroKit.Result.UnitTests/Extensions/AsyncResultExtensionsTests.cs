using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Extensions;

/// <summary>The three surfaces of each async extension.</summary>
public enum AsyncSurface
{
    /// <summary><c>XxxAsync</c> on <see cref="Result{T}"/>, with an async delegate.</summary>
    ResultWithAsyncDelegate,

    /// <summary><c>Xxx</c> on <see cref="Task{TResult}"/> of <see cref="Result{T}"/>.</summary>
    TaskOfResult,

    /// <summary><c>Xxx</c> on <see cref="ValueTask{TResult}"/> of <see cref="Result{T}"/>.</summary>
    ValueTaskOfResult,
}

/// <summary>
/// How the asynchronous part completes: the delegate on <see cref="AsyncSurface.ResultWithAsyncDelegate"/>,
/// the source task on the two task surfaces.
/// </summary>
public enum AsyncCompletion
{
    /// <summary>Already completed when the extension reads it.</summary>
    Synchronous,

    /// <summary>Completes after a real <see cref="Task.Yield"/>.</summary>
    Asynchronous,
}

public sealed class AsyncResultExtensionsTests
{

    private static readonly string[] Methods =
        ["Map", "Bind", "Match", "Tap", "TapError", "Ensure", "MapError", "Compensate", "Finally"];

    private static readonly string[] NullArguments =
    [
        "Map.mapper", "Bind.binder", "Match.onSuccess", "Match.onFailure", "Tap.action", "TapError.action",
        "Ensure.predicate", "Ensure.error", "MapError.mapper", "Compensate.fallback", "Finally.action",
    ];

    private static readonly AsyncSurface[] TaskSurfaceValues = [AsyncSurface.TaskOfResult, AsyncSurface.ValueTaskOfResult];

    public static TheoryData<AsyncSurface, AsyncCompletion> AllSurfaces
    {
        get
        {
            var data = new TheoryData<AsyncSurface, AsyncCompletion>();
            foreach (var surface in Enum.GetValues<AsyncSurface>())
            {
                foreach (var completion in Enum.GetValues<AsyncCompletion>())
                    data.Add(surface, completion);
            }

            return data;
        }
    }

    public static TheoryData<AsyncSurface> TaskSurfaces
    {
        get
        {
            var data = new TheoryData<AsyncSurface>();
            foreach (var surface in TaskSurfaceValues)
                data.Add(surface);
            return data;
        }
    }

    public static TheoryData<string, AsyncSurface, AsyncCompletion> MethodsOnAllSurfaces
    {
        get
        {
            var data = new TheoryData<string, AsyncSurface, AsyncCompletion>();
            foreach (var method in Methods)
            {
                foreach (var surface in Enum.GetValues<AsyncSurface>())
                {
                    foreach (var completion in Enum.GetValues<AsyncCompletion>())
                        data.Add(method, surface, completion);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, AsyncSurface, AsyncCompletion> MethodsOnTaskSurfaces
    {
        get
        {
            var data = new TheoryData<string, AsyncSurface, AsyncCompletion>();
            foreach (var method in Methods)
            {
                foreach (var surface in TaskSurfaceValues)
                {
                    foreach (var completion in Enum.GetValues<AsyncCompletion>())
                        data.Add(method, surface, completion);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, AsyncSurface> MethodsOnPendingTaskSurfaces
    {
        get
        {
            var data = new TheoryData<string, AsyncSurface>();
            foreach (var method in Methods)
            {
                foreach (var surface in TaskSurfaceValues)
                    data.Add(method, surface);
            }

            return data;
        }
    }

    public static TheoryData<string, AsyncSurface> NullArgumentsOnAllSurfaces
    {
        get
        {
            var data = new TheoryData<string, AsyncSurface>();
            foreach (var argument in NullArguments)
            {
                foreach (var surface in Enum.GetValues<AsyncSurface>())
                    data.Add(argument, surface);
            }

            return data;
        }
    }

    #region Map

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Map_WhenSuccess_TransformsValue(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var mapped = await MapOn(surface, completion, result, x => x * 2);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(42);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Map_WhenFailure_PropagatesSameErrorWithoutInvokingMapper(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var mapped = await MapOn(surface, completion, result, x =>
        {
            called = true;
            return x;
        });

        // Assert
        called.ShouldBeFalse();
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(error);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Map_WhenDefault_ThrowsResultException(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        await Should.ThrowAsync<ResultException>(async () => await MapOn(surface, completion, result, x =>
        {
            called = true;
            return x;
        }));
        called.ShouldBeFalse();
    }

    #endregion

    #region Bind

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Bind_WhenSuccessAndBinderSucceeds_ReturnsBinderResult(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var bound = await BindOn(surface, completion, result, x => Result.Success(x * 2));

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe(42);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Bind_WhenSuccessAndBinderFails_ReturnsBinderError(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        var bound = await BindOn(surface, completion, result, _ => Result.Failure<int>(error));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Bind_WhenFailure_PropagatesSameErrorWithoutInvokingBinder(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var bound = await BindOn(surface, completion, result, x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        called.ShouldBeFalse();
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Bind_WhenDefault_ThrowsResultException(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        await Should.ThrowAsync<ResultException>(async () => await BindOn(surface, completion, result, x =>
        {
            called = true;
            return Result.Success(x);
        }));
        called.ShouldBeFalse();
    }

    #endregion

    #region Match

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Match_WhenSuccess_ReturnsOnSuccessValueWithoutInvokingOnFailure(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var failureCalled = false;
        var result = Result.Success(5);

        // Act
        var matched = await MatchOn(
            surface,
            completion,
            result,
            v => $"value {v}",
            _ =>
            {
                failureCalled = true;
                return "failure";
            });

        // Assert
        matched.ShouldBe("value 5");
        failureCalled.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Match_WhenFailure_PassesSameErrorToOnFailureWithoutInvokingOnSuccess(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var successCalled = false;
        IError? received = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var matched = await MatchOn(
            surface,
            completion,
            result,
            _ =>
            {
                successCalled = true;
                return "success";
            },
            e =>
            {
                received = e;
                return "failure";
            });

        // Assert
        matched.ShouldBe("failure");
        received.ShouldBeSameAs(error);
        successCalled.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Match_WhenDefault_ThrowsResultExceptionWithoutInvokingEitherBranch(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        await Should.ThrowAsync<ResultException>(async () => await MatchOn(
            surface,
            completion,
            result,
            _ =>
            {
                called = true;
                return "success";
            },
            _ =>
            {
                called = true;
                return "failure";
            }));
        called.ShouldBeFalse();
    }

    #endregion

    #region Tap

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Tap_WhenSuccess_InvokesActionWithValueAndReturnsSameResult(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var seen = 0;
        var result = Result.Success(42);

        // Act
        var tapped = await TapOn(surface, completion, result, v => seen = v);

        // Assert
        seen.ShouldBe(42);
        tapped.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Tap_WhenFailure_DoesNotInvokeActionAndPropagatesSameError(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = await TapOn(surface, completion, result, _ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Tap_WhenDefault_DoesNothingAndReturnsDefault(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var tapped = await TapOn(surface, completion, result, _ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.ShouldBe(default(Result<int>));
    }

    [Theory]
    [MemberData(nameof(TaskSurfaces))]
    public async Task Tap_WithAsyncLambda_AwaitsSideEffectBeforeReturning(AsyncSurface surface)
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success(42);

        // Act
        var tapped = await TapWithAsyncLambdaOn(surface, result, async () =>
        {
            await Task.Yield();
            sideEffectRan = true;
        });

        // Assert
        sideEffectRan.ShouldBeTrue();
        tapped.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(TaskSurfaces))]
    public async Task Tap_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait(AsyncSurface surface)
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(42);

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TapWithAsyncLambdaOn(surface, result, async () =>
            {
                await Task.Yield();
                throw boom;
            }));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region TapError

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task TapError_WhenFailure_InvokesActionWithSameErrorAndReturnsSameResult(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        IError? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var tapped = await TapErrorOn(surface, completion, result, e => seen = e);

        // Assert
        seen.ShouldBeSameAs(error);
        tapped.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task TapError_WhenSuccess_DoesNotInvokeActionAndKeepsValue(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var tapped = await TapErrorOn(surface, completion, result, _ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.IsSuccess.ShouldBeTrue();
        tapped.Value.ShouldBe(42);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task TapError_WhenDefault_DoesNothingAndReturnsDefault(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var tapped = await TapErrorOn(surface, completion, result, _ => called = true);

        // Assert
        called.ShouldBeFalse();
        tapped.ShouldBe(default(Result<int>));
    }

    [Theory]
    [MemberData(nameof(TaskSurfaces))]
    public async Task TapError_WithAsyncLambda_AwaitsSideEffectBeforeReturning(AsyncSurface surface)
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Failure<int>(new TestError());

        // Act
        var tapped = await TapErrorWithAsyncLambdaOn(surface, result, async () =>
        {
            await Task.Yield();
            sideEffectRan = true;
        });

        // Assert
        sideEffectRan.ShouldBeTrue();
        tapped.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(TaskSurfaces))]
    public async Task TapError_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait(AsyncSurface surface)
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Failure<int>(new TestError());

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TapErrorWithAsyncLambdaOn(surface, result, async () =>
            {
                await Task.Yield();
                throw boom;
            }));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Ensure

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Ensure_WhenPredicateTrue_ReturnsSameResult(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var result = Result.Success(10);

        // Act
        var ensured = await EnsureOn(surface, completion, result, x => x > 5, new TestError());

        // Assert
        ensured.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Ensure_WhenPredicateFalse_ReturnsFailureWithSameError(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var error = new TestError("too small");
        var result = Result.Success(1);

        // Act
        var ensured = await EnsureOn(surface, completion, result, x => x > 5, error);

        // Assert
        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBeSameAs(error);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Ensure_WhenFailure_PropagatesOriginalErrorWithoutInvokingPredicate(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        var ensured = await EnsureOn(
            surface,
            completion,
            result,
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

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Ensure_WhenDefault_ThrowsResultException(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        await Should.ThrowAsync<ResultException>(async () => await EnsureOn(
            surface,
            completion,
            result,
            _ =>
            {
                called = true;
                return true;
            },
            new TestError()));
        called.ShouldBeFalse();
    }

    #endregion

    #region MapError

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task MapError_WhenFailure_ReturnsFailureWithMappedError(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        IError? received = null;
        var original = new TestError();
        var replacement = new OtherError();
        var result = Result.Failure<int>(original);

        // Act
        var mapped = await MapErrorOn(surface, completion, result, e =>
        {
            received = e;
            return replacement;
        });

        // Assert
        received.ShouldBeSameAs(original);
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(replacement);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task MapError_WhenSuccess_ReturnsSameResultWithoutInvokingMapper(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var mapped = await MapErrorOn(surface, completion, result, e =>
        {
            called = true;
            return e;
        });

        // Assert
        called.ShouldBeFalse();
        mapped.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task MapError_WhenDefault_ReturnsDefaultWithoutInvokingMapper(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var mapped = await MapErrorOn(surface, completion, result, e =>
        {
            called = true;
            return e;
        });

        // Assert
        called.ShouldBeFalse();
        mapped.ShouldBe(default(Result<int>));
    }

    #endregion

    #region Compensate

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Compensate_WhenFailure_ReturnsFallbackResult(AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        IError? received = null;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        var compensated = await CompensateOn(surface, completion, result, e =>
        {
            received = e;
            return Result.Success(-1);
        });

        // Assert
        received.ShouldBeSameAs(original);
        compensated.IsSuccess.ShouldBeTrue();
        compensated.Value.ShouldBe(-1);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Compensate_WhenFailureAndFallbackFails_ReturnsFallbackError(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var fallbackError = new OtherError();
        var result = Result.Failure<int>(new TestError());

        // Act
        var compensated = await CompensateOn(surface, completion, result, _ => Result.Failure<int>(fallbackError));

        // Assert
        compensated.IsFailure.ShouldBeTrue();
        compensated.Error.ShouldBeSameAs(fallbackError);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Compensate_WhenSuccess_ReturnsSameResultWithoutInvokingFallback(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        var result = Result.Success(42);

        // Act
        var compensated = await CompensateOn(surface, completion, result, _ =>
        {
            called = true;
            return Result.Success(0);
        });

        // Assert
        called.ShouldBeFalse();
        compensated.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Compensate_WhenDefault_ReturnsDefaultWithoutInvokingFallback(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        var compensated = await CompensateOn(surface, completion, result, _ =>
        {
            called = true;
            return Result.Success(0);
        });

        // Assert
        called.ShouldBeFalse();
        compensated.ShouldBe(default(Result<int>));
    }

    #endregion

    #region Finally

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Finally_WhenSuccess_InvokesActionWithResultAndReturnsSameResult(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        Result<int>? seen = null;
        var result = Result.Success(42);

        // Act
        var finished = await FinallyOn(surface, completion, result, r => seen = r);

        // Assert
        seen.ShouldBe(result);
        finished.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Finally_WhenFailure_InvokesActionWithResultAndReturnsSameResult(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        Result<int>? seen = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var finished = await FinallyOn(surface, completion, result, r => seen = r);

        // Assert
        seen.ShouldBe(result);
        finished.IsFailure.ShouldBeTrue();
        finished.Error.ShouldBeSameAs(error);
    }

    [Theory]
    [MemberData(nameof(AllSurfaces))]
    public async Task Finally_WhenDefault_InvokesActionWithDefaultAndReturnsDefault(
        AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        Result<int>? seen = null;
        Result<int> result = default;

        // Act
        var finished = await FinallyOn(surface, completion, result, r => seen = r);

        // Assert
        seen.ShouldBe(default(Result<int>));
        finished.ShouldBe(default(Result<int>));
    }

    [Theory]
    [MemberData(nameof(TaskSurfaces))]
    public async Task Finally_WithAsyncLambda_AwaitsSideEffectBeforeReturning(AsyncSurface surface)
    {
        // Arrange
        var sideEffectRan = false;
        var result = Result.Success(42);

        // Act
        var finished = await FinallyWithAsyncLambdaOn(surface, result, async () =>
        {
            await Task.Yield();
            sideEffectRan = true;
        });

        // Assert
        sideEffectRan.ShouldBeTrue();
        finished.ShouldBe(result);
    }

    [Theory]
    [MemberData(nameof(TaskSurfaces))]
    public async Task Finally_WithAsyncLambda_WhenSideEffectThrows_SurfacesExceptionAtAwait(AsyncSurface surface)
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var result = Result.Success(42);

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await FinallyWithAsyncLambdaOn(surface, result, async () =>
            {
                await Task.Yield();
                throw boom;
            }));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Every method

    [Theory]
    [MemberData(nameof(MethodsOnAllSurfaces))]
    public async Task Extension_WhenDelegateThrows_PropagatesSameException(
        string method, AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        var source = SourceTask(ResultInvokingDelegateOf(method), completion);

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Invoke(method, surface, completion, source, () => throw boom));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    [Theory]
    [MemberData(nameof(MethodsOnAllSurfaces))]
    public async Task Extension_WhenDelegateThrowsOperationCanceledException_PropagatesCancellation(
        string method, AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var canceled = new OperationCanceledException(cts.Token);
        var source = SourceTask(ResultInvokingDelegateOf(method), completion);

        // Act
        // Record.ExceptionAsync awaits the task itself: Should.ThrowAsync reports a canceled task as a new
        // TaskCanceledException, not as the exception the delegate threw.
        var thrown = await Record.ExceptionAsync(async () =>
            await Invoke(method, surface, completion, source, () => throw canceled));

        // Assert
        thrown.ShouldBeSameAs(canceled);
    }

    [Theory]
    [MemberData(nameof(MethodsOnTaskSurfaces))]
    public async Task Extension_WhenSourceTaskFaults_PropagatesExceptionWithoutInvokingDelegate(
        string method, AsyncSurface surface, AsyncCompletion completion)
    {
        // Arrange
        var invoked = false;
        var boom = new InvalidOperationException("boom");
        var source = completion == AsyncCompletion.Synchronous
            ? Task.FromException<Result<int>>(boom)
            : AfterYield<Result<int>>(() => throw boom);

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Invoke(method, surface, completion, source, () => invoked = true));

        // Assert
        thrown.ShouldBeSameAs(boom);
        invoked.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(MethodsOnPendingTaskSurfaces))]
    public async Task Extension_WhenSourceNotYetCompleted_DefersDelegateUntilCompletion(
        string method, AsyncSurface surface)
    {
        // Arrange
        var invoked = false;
        var source = new TaskCompletionSource<Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Invoke(method, surface, AsyncCompletion.Synchronous, source.Task, () => invoked = true);
        var completedBeforeSource = pending.IsCompleted;
        var invokedBeforeSource = invoked;

        // Act
        source.SetResult(ResultInvokingDelegateOf(method));
        await pending;

        // Assert
        completedBeforeSource.ShouldBeFalse();
        invokedBeforeSource.ShouldBeFalse();
        invoked.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(NullArgumentsOnAllSurfaces))]
    public async Task Extension_WhenAwaitedWithNullArgument_ThrowsArgumentNullException(
        string argument, AsyncSurface surface)
    {
        // Act
        var thrown = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await CallWithNullArgument(argument, surface));

        // Assert
        thrown.ParamName.ShouldBe(ParamNameOf(argument));
    }

    [Theory]
    [MemberData(nameof(NullArgumentsOnAllSurfaces))]
    public void Extension_WithNullArgument_ThrowsBeforeReturningTask(string argument, AsyncSurface surface)
    {
        // Arrange
        Task? pending = null;

        // Act
        var thrown = Record.Exception(() =>
        {
            pending = CallWithNullArgument(argument, surface);
        });

        // Assert
        pending.ShouldBeNull();
        thrown.ShouldBeOfType<ArgumentNullException>().ParamName.ShouldBe(ParamNameOf(argument));
    }

    #endregion

    #region Chained

    [Fact]
    public async Task Pipeline_WhenEveryStepSucceeds_FlowsAcrossSurfacesOnSuccessRail()
    {
        // Arrange
        var tapped = new List<int>();
        var start = Result.Success(2);

        // Act
        var outcome = await start
            .MapAsync(async x =>
            {
                await Task.Yield();
                return x + 1;
            })
            .Bind(x => Result.Success(x * 10))
            .Ensure(x => x > 10, new TestError("too small"))
            .Tap(tapped.Add)
            .Match(x => $"value {x}", e => e.Code.Value);

        // Assert
        outcome.ShouldBe("value 30");
        tapped.ShouldHaveSingleItem().ShouldBe(30);
    }

    [Fact]
    public async Task Pipeline_WhenStepFails_ShortCircuitsRemainingStepsAndCompensates()
    {
        // Arrange
        var afterFailureCalls = 0;
        IError? tappedError = null;
        var error = new NotFoundTestError(7);
        var start = Result.Success(7);

        // Act
        var outcome = await start
            .BindAsync(async _ =>
            {
                await Task.Yield();
                return Result.Failure<int>(error);
            })
            .Map(x =>
            {
                afterFailureCalls++;
                return x * 2;
            })
            .Tap(_ => afterFailureCalls++)
            .TapError(e => tappedError = e)
            .Compensate(_ => Result.Success(0));

        // Assert
        afterFailureCalls.ShouldBe(0);
        tappedError.ShouldBeSameAs(error);
        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.ShouldBe(0);
    }

    #endregion

    #region Surface dispatch

    private static ValueTask<Result<int>> MapOn(
        AsyncSurface surface, AsyncCompletion completion, Result<int> source, Func<int, int> mapper) => surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => source.MapAsync(v => Deliver(completion, () => mapper(v))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Map(mapper),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Map(mapper),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> BindOn(
        AsyncSurface surface, AsyncCompletion completion, Result<int> source, Func<int, Result<int>> binder) =>
        surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => source.BindAsync(v => Deliver(completion, () => binder(v))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Bind(binder),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Bind(binder),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<string> MatchOn(
        AsyncSurface surface,
        AsyncCompletion completion,
        Result<int> source,
        Func<int, string> onSuccess,
        Func<IError, string> onFailure) => surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => source.MatchAsync(
                v => Deliver(completion, () => onSuccess(v)),
                e => Deliver(completion, () => onFailure(e))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Match(onSuccess, onFailure),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Match(onSuccess, onFailure),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> TapOn(
        AsyncSurface surface, AsyncCompletion completion, Result<int> source, Action<int> action) => surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => source.TapAsync(v => DeliverAction(completion, () => action(v))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Tap(action),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Tap(action),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> TapErrorOn(
        AsyncSurface surface, AsyncCompletion completion, Result<int> source, Action<IError> action) =>
        surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate =>
                source.TapErrorAsync(e => DeliverAction(completion, () => action(e))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).TapError(action),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).TapError(action),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> EnsureOn(
        AsyncSurface surface,
        AsyncCompletion completion,
        Result<int> source,
        Func<int, bool> predicate,
        IError error) => surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate =>
                source.EnsureAsync(v => Deliver(completion, () => predicate(v)), error),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Ensure(predicate, error),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Ensure(predicate, error),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> MapErrorOn(
        AsyncSurface surface, AsyncCompletion completion, Result<int> source, Func<IError, IError> mapper) =>
        surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => source.MapErrorAsync(e => Deliver(completion, () => mapper(e))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).MapError(mapper),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).MapError(mapper),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> CompensateOn(
        AsyncSurface surface,
        AsyncCompletion completion,
        Result<int> source,
        Func<IError, Result<int>> fallback) => surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate =>
                source.CompensateAsync(e => Deliver(completion, () => fallback(e))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Compensate(fallback),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Compensate(fallback),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static ValueTask<Result<int>> FinallyOn(
        AsyncSurface surface, AsyncCompletion completion, Result<int> source, Action<Result<int>> action) =>
        surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate =>
                source.FinallyAsync(r => DeliverAction(completion, () => action(r))),
            AsyncSurface.TaskOfResult => SourceTask(source, completion).Finally(action),
            AsyncSurface.ValueTaskOfResult => SourceValueTask(source, completion).Finally(action),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    // The async lambda binds to the Action overload today (async void), and to the awaited
    // Func<..., Task> overload once Phase 2 adds it; AsyncSettle compiles against both.
    private static ValueTask<Result<int>> TapWithAsyncLambdaOn(
        AsyncSurface surface, Result<int> source, Func<Task> effect) => surface == AsyncSurface.TaskOfResult
            ? AsyncSettle.Settle(Task.FromResult(source).Tap(async _ => await effect()))
            : AsyncSettle.Settle(ValueTask.FromResult(source).Tap(async _ => await effect()));

    private static ValueTask<Result<int>> TapErrorWithAsyncLambdaOn(
        AsyncSurface surface, Result<int> source, Func<Task> effect) => surface == AsyncSurface.TaskOfResult
            ? AsyncSettle.Settle(Task.FromResult(source).TapError(async _ => await effect()))
            : AsyncSettle.Settle(ValueTask.FromResult(source).TapError(async _ => await effect()));

    private static ValueTask<Result<int>> FinallyWithAsyncLambdaOn(
        AsyncSurface surface, Result<int> source, Func<Task> effect) => surface == AsyncSurface.TaskOfResult
            ? AsyncSettle.Settle(Task.FromResult(source).Finally(async _ => await effect()))
            : AsyncSettle.Settle(ValueTask.FromResult(source).Finally(async _ => await effect()));

    // Runs `method` on `surface` with a delegate that calls `hook`; the source result is chosen so the
    // delegate runs (a failure for the methods of the failure rail).
    private static Task Invoke(
        string method, AsyncSurface surface, AsyncCompletion completion, Task<Result<int>> source, Action hook) =>
        surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => InvokeWithAsyncDelegateAsync(method, completion, source, hook),
            AsyncSurface.TaskOfResult => InvokeOnTask(method, source, hook),
            AsyncSurface.ValueTaskOfResult => InvokeOnValueTask(method, new ValueTask<Result<int>>(source), hook),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    private static async Task InvokeWithAsyncDelegateAsync(
        string method, AsyncCompletion completion, Task<Result<int>> source, Action hook)
    {
        var result = await source;
        Task pending = method switch
        {
            "Map" => result.MapAsync(v => Deliver(completion, () =>
            {
                hook();
                return v;
            })).AsTask(),
            "Bind" => result.BindAsync(v => Deliver(completion, () =>
            {
                hook();
                return Result.Success(v);
            })).AsTask(),
            "Match" => result.MatchAsync(
                v => Deliver(completion, () =>
                {
                    hook();
                    return v;
                }),
                _ => Deliver(completion, () =>
                {
                    hook();
                    return 0;
                })).AsTask(),
            "Tap" => result.TapAsync(_ => DeliverAction(completion, hook)).AsTask(),
            "TapError" => result.TapErrorAsync(_ => DeliverAction(completion, hook)).AsTask(),
            "Ensure" => result.EnsureAsync(
                _ => Deliver(completion, () =>
                {
                    hook();
                    return true;
                }),
                new TestError()).AsTask(),
            "MapError" => result.MapErrorAsync(e => Deliver(completion, () =>
            {
                hook();
                return e;
            })).AsTask(),
            "Compensate" => result.CompensateAsync(_ => Deliver(completion, () =>
            {
                hook();
                return Result.Success(0);
            })).AsTask(),
            "Finally" => result.FinallyAsync(_ => DeliverAction(completion, hook)).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
        await pending;
    }

    private static Task InvokeOnTask(string method, Task<Result<int>> source, Action hook) => method switch
    {
        "Map" => source.Map(v =>
        {
            hook();
            return v;
        }).AsTask(),
        "Bind" => source.Bind(v =>
        {
            hook();
            return Result.Success(v);
        }).AsTask(),
        "Match" => source.Match(
            v =>
            {
                hook();
                return v;
            },
            _ =>
            {
                hook();
                return 0;
            }).AsTask(),
        "Tap" => source.Tap(_ => hook()).AsTask(),
        "TapError" => source.TapError(_ => hook()).AsTask(),
        "Ensure" => source.Ensure(
            _ =>
            {
                hook();
                return true;
            },
            new TestError()).AsTask(),
        "MapError" => source.MapError(e =>
        {
            hook();
            return e;
        }).AsTask(),
        "Compensate" => source.Compensate(_ =>
        {
            hook();
            return Result.Success(0);
        }).AsTask(),
        "Finally" => source.Finally(_ => hook()).AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private static Task InvokeOnValueTask(string method, ValueTask<Result<int>> source, Action hook) => method switch
    {
        "Map" => source.Map(v =>
        {
            hook();
            return v;
        }).AsTask(),
        "Bind" => source.Bind(v =>
        {
            hook();
            return Result.Success(v);
        }).AsTask(),
        "Match" => source.Match(
            v =>
            {
                hook();
                return v;
            },
            _ =>
            {
                hook();
                return 0;
            }).AsTask(),
        "Tap" => source.Tap(_ => hook()).AsTask(),
        "TapError" => source.TapError(_ => hook()).AsTask(),
        "Ensure" => source.Ensure(
            _ =>
            {
                hook();
                return true;
            },
            new TestError()).AsTask(),
        "MapError" => source.MapError(e =>
        {
            hook();
            return e;
        }).AsTask(),
        "Compensate" => source.Compensate(_ =>
        {
            hook();
            return Result.Success(0);
        }).AsTask(),
        "Finally" => source.Finally(_ => hook()).AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    // Delegates are cast where Phase 2 adds a same-name Func<..., Task> overload, so null stays unambiguous.
    private static Task CallWithNullArgument(string argument, AsyncSurface surface)
    {
        var result = Result.Success(1);
        return surface switch
        {
            AsyncSurface.ResultWithAsyncDelegate => CallWithNullArgumentOnResult(argument, result),
            AsyncSurface.TaskOfResult => CallWithNullArgumentOnTask(argument, Task.FromResult(result)),
            AsyncSurface.ValueTaskOfResult => CallWithNullArgumentOnValueTask(argument, ValueTask.FromResult(result)),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
    }

    private static Task CallWithNullArgumentOnResult(string argument, Result<int> result) => argument switch
    {
        "Map.mapper" => result.MapAsync<int, int>(null!).AsTask(),
        "Bind.binder" => result.BindAsync<int, int>(null!).AsTask(),
        "Match.onSuccess" => result.MatchAsync<int, int>(null!, _ => Task.FromResult(0)).AsTask(),
        "Match.onFailure" => result.MatchAsync<int, int>(v => Task.FromResult(v), null!).AsTask(),
        "Tap.action" => result.TapAsync((Func<int, Task>)null!).AsTask(),
        "TapError.action" => result.TapErrorAsync((Func<IError, Task>)null!).AsTask(),
        "Ensure.predicate" => result.EnsureAsync(null!, new TestError()).AsTask(),
        "Ensure.error" => result.EnsureAsync(_ => Task.FromResult(true), null!).AsTask(),
        "MapError.mapper" => result.MapErrorAsync(null!).AsTask(),
        "Compensate.fallback" => result.CompensateAsync(null!).AsTask(),
        "Finally.action" => result.FinallyAsync((Func<Result<int>, Task>)null!).AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(argument)),
    };

    private static Task CallWithNullArgumentOnTask(string argument, Task<Result<int>> source) => argument switch
    {
        "Map.mapper" => source.Map<int, int>(null!).AsTask(),
        "Bind.binder" => source.Bind<int, int>(null!).AsTask(),
        "Match.onSuccess" => source.Match<int, int>(null!, _ => 0).AsTask(),
        "Match.onFailure" => source.Match<int, int>(v => v, null!).AsTask(),
        "Tap.action" => source.Tap((Action<int>)null!).AsTask(),
        "TapError.action" => source.TapError((Action<IError>)null!).AsTask(),
        "Ensure.predicate" => source.Ensure(null!, new TestError()).AsTask(),
        "Ensure.error" => source.Ensure(_ => true, null!).AsTask(),
        "MapError.mapper" => source.MapError(null!).AsTask(),
        "Compensate.fallback" => source.Compensate(null!).AsTask(),
        "Finally.action" => source.Finally((Action<Result<int>>)null!).AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(argument)),
    };

    private static Task CallWithNullArgumentOnValueTask(string argument, ValueTask<Result<int>> source) =>
        argument switch
        {
            "Map.mapper" => source.Map<int, int>(null!).AsTask(),
            "Bind.binder" => source.Bind<int, int>(null!).AsTask(),
            "Match.onSuccess" => source.Match<int, int>(null!, _ => 0).AsTask(),
            "Match.onFailure" => source.Match<int, int>(v => v, null!).AsTask(),
            "Tap.action" => source.Tap((Action<int>)null!).AsTask(),
            "TapError.action" => source.TapError((Action<IError>)null!).AsTask(),
            "Ensure.predicate" => source.Ensure(null!, new TestError()).AsTask(),
            "Ensure.error" => source.Ensure(_ => true, null!).AsTask(),
            "MapError.mapper" => source.MapError(null!).AsTask(),
            "Compensate.fallback" => source.Compensate(null!).AsTask(),
            "Finally.action" => source.Finally((Action<Result<int>>)null!).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(argument)),
        };

    private static string ParamNameOf(string argument) => argument.Split('.')[1];

    private static Result<int> ResultInvokingDelegateOf(string method) =>
        method is "TapError" or "MapError" or "Compensate"
            ? Result.Failure<int>(new TestError())
            : Result.Success(1);

    private static Task<Result<int>> SourceTask(Result<int> result, AsyncCompletion completion) =>
        completion == AsyncCompletion.Synchronous ? Task.FromResult(result) : AfterYield(() => result);

    private static ValueTask<Result<int>> SourceValueTask(Result<int> result, AsyncCompletion completion) =>
        completion == AsyncCompletion.Synchronous
            ? ValueTask.FromResult(result)
            : new ValueTask<Result<int>>(AfterYield(() => result));

    private static Task<T> Deliver<T>(AsyncCompletion completion, Func<T> body) =>
        completion == AsyncCompletion.Synchronous ? Task.FromResult(body()) : AfterYield(body);

    private static Task DeliverAction(AsyncCompletion completion, Action body)
    {
        if (completion == AsyncCompletion.Asynchronous)
            return AfterYield(body);

        body();
        return Task.CompletedTask;
    }

    private static async Task<T> AfterYield<T>(Func<T> body)
    {
        await Task.Yield();
        return body();
    }

    private static async Task AfterYield(Action body)
    {
        await Task.Yield();
        body();
    }

    #endregion
}
