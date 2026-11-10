using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Extensions;

// ADR-RESULT-001 D8: on Result<T>, every async operation has a (…, CancellationToken) => ValueTask overload with
// ct = default last, passed to the delegate; D9 adds BindAsync from a value to a command, in both forms.
// Overload binding is decided by arity at compile time: a one-parameter lambda (async v => …) binds to the Task
// overload, a two-parameter lambda (async (v, ct) => …) to the token overload. The Overload region proves it with
// delegates that compile against one overload only.
public sealed class CancellationOverloadTests
{
    private static readonly string[] Operations =
        ["Map", "Bind", "BindToCommand", "Match", "Tap", "TapError", "Ensure", "MapError", "Compensate", "Finally"];

    private static readonly string[] NullArguments =
    [
        "Map.mapper", "Bind.binder", "BindToCommand.binder", "Match.onSuccess", "Match.onFailure", "Tap.action",
        "TapError.action", "Ensure.predicate", "Ensure.error", "MapError.mapper", "Compensate.fallback", "Finally.action",
    ];

    public static TheoryData<string> OperationNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var operation in Operations)
                data.Add(operation);
            return data;
        }
    }

    public static TheoryData<string> NullArgumentNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var argument in NullArguments)
                data.Add(argument);
            return data;
        }
    }

    #region MapAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapAsync_WithToken_WhenSuccess_MapsValueAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success(21);

        // Act
        Result<int> mapped = await result.MapAsync(async (v, ct) =>
        {
            seen = ct;
            return await LaterValue(yield, v * 2);
        }, cts.Token);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(42);
        seen.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task MapAsync_WithToken_WhenFailure_PropagatesSameErrorWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result<int> mapped = await result.MapAsync((v, _) =>
        {
            called = true;
            return ValueTask.FromResult(v);
        }, CancellationToken.None);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task MapAsync_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result<int>>> act = async () => await result.MapAsync((v, _) =>
        {
            called = true;
            return ValueTask.FromResult(v);
        });
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    #endregion

    #region BindAsync (Result<TIn> -> Result<TOut>)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsync_WithToken_WhenSuccess_ReturnsBinderResultAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success(5);

        // Act
        Result<string> bound = await result.BindAsync(async (v, ct) =>
        {
            seen = ct;
            return await LaterValue(yield, Result.Success($"#{v}"));
        }, cts.Token);

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe("#5");
        seen.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task BindAsync_WithToken_WhenSuccessAndBinderFails_ReturnsBinderFailure()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(5);

        // Act
        Result<string> bound = await result.BindAsync((_, _) => ValueTask.FromResult(Result.Failure<string>(error)));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task BindAsync_WithToken_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result<string> bound = await result.BindAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(Result.Success("x"));
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task BindAsync_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result<string>>> act = async () => await result.BindAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(Result.Success("x"));
        });
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    #endregion

    #region BindAsync (Result<TIn> -> Result)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsyncToCommand_WhenSuccess_PassesValueAndReturnsBinderResult(bool yield)
    {
        // Arrange
        var seen = 0;
        var result = Result.Success(9);

        // Act
        Result bound = await result.BindAsync(async v =>
        {
            seen = v;
            return await Later(yield, Result.Success());
        });

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        seen.ShouldBe(9);
    }

    [Fact]
    public async Task BindAsyncToCommand_WhenSuccessAndBinderFails_ReturnsBinderFailure()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(9);

        // Act
        Result bound = await result.BindAsync(_ => Task.FromResult(Result.Failure(error)));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task BindAsyncToCommand_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result bound = await result.BindAsync(_ =>
        {
            called = true;
            return Task.FromResult(Result.Success());
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task BindAsyncToCommand_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result>> act = async () => await result.BindAsync(_ =>
        {
            called = true;
            return Task.FromResult(Result.Success());
        });
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    [Fact]
    public void BindAsyncToCommand_WhenBinderIsNull_ThrowsArgumentNullExceptionAtTheCall()
    {
        // Arrange
        var result = Result.Success(1);

        // Act
        var thrown = Record.Exception(() =>
        {
            ValueTask<Result> pending = result.BindAsync((Func<int, Task<Result>>)null!);
            _ = pending.AsTask();
        });

        // Assert
        thrown.ShouldBeOfType<ArgumentNullException>().ParamName.ShouldBe("binder");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindAsyncToCommand_WithToken_WhenSuccess_PassesValueAndToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        var seenValue = 0;
        var result = Result.Success(9);

        // Act
        Result bound = await result.BindAsync(async (v, ct) =>
        {
            seenValue = v;
            seenToken = ct;
            return await LaterValue(yield, Result.Success());
        }, cts.Token);

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        seenValue.ShouldBe(9);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task BindAsyncToCommand_WithToken_WhenFailure_PropagatesSameErrorWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result bound = await result.BindAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(Result.Success());
        });

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task BindAsyncToCommand_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingBinder()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result>> act = async () => await result.BindAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(Result.Success());
        });
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    #endregion

    #region MatchAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WithToken_WhenSuccess_RunsOnSuccessWithValueAndToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var result = Result.Success(3);

        // Act
        var matched = await result.MatchAsync(
            async (v, ct) =>
            {
                seen = ct;
                return await LaterValue(yield, $"ok {v}");
            },
            (_, _) => ValueTask.FromResult("ko"),
            cts.Token);

        // Assert
        matched.ShouldBe("ok 3");
        seen.ShouldBe(cts.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchAsync_WithToken_WhenFailure_RunsOnFailureWithSameErrorAndToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        IError? seenError = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var matched = await result.MatchAsync(
            (_, _) => ValueTask.FromResult("ok"),
            async (e, ct) =>
            {
                seenError = e;
                seenToken = ct;
                return await LaterValue(yield, "ko");
            },
            cts.Token);

        // Assert
        matched.ShouldBe("ko");
        seenError.ShouldBeSameAs(error);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task MatchAsync_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingEitherBranch()
    {
        // Arrange
        var calls = 0;
        Result<int> result = default;

        // Act + Assert
        Func<Task<int>> act = async () => await result.MatchAsync(
            (v, _) =>
            {
                calls++;
                return ValueTask.FromResult(v);
            },
            (_, _) =>
            {
                calls++;
                return ValueTask.FromResult(-1);
            });
        await Should.ThrowAsync<ResultException>(act);
        calls.ShouldBe(0);
    }

    #endregion

    #region TapAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_WithToken_WhenSuccess_AwaitsEffectAndReturnsSameResult(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        var seenValue = 0;
        var result = Result.Success(4);

        // Act
        Result<int> tapped = await result.TapAsync(async (v, ct) =>
        {
            await LaterValue(yield, 0);
            seenValue = v;
            seenToken = ct;
        }, cts.Token);

        // Assert
        tapped.ShouldBe(result);
        tapped.IsSuccess.ShouldBeTrue();
        seenValue.ShouldBe(4);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task TapAsync_WithToken_WhenFailure_ReturnsSameFailureWithoutCallingEffect()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result<int> tapped = await result.TapAsync((_, _) =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task TapAsync_WithToken_WhenDefault_ReturnsDefaultWithoutCallingEffect()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        Result<int> tapped = await result.TapAsync((_, _) =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        tapped.IsSuccess.ShouldBeFalse();
        tapped.IsFailure.ShouldBeFalse();
        called.ShouldBeFalse();
    }

    #endregion

    #region TapErrorAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapErrorAsync_WithToken_WhenFailure_AwaitsEffectWithSameErrorAndToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        IError? seenError = null;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        Result<int> tapped = await result.TapErrorAsync(async (e, ct) =>
        {
            await LaterValue(yield, 0);
            seenError = e;
            seenToken = ct;
        }, cts.Token);

        // Assert
        tapped.IsFailure.ShouldBeTrue();
        tapped.Error.ShouldBeSameAs(error);
        seenError.ShouldBeSameAs(error);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task TapErrorAsync_WithToken_WhenSuccess_ReturnsSuccessWithoutCallingEffect()
    {
        // Arrange
        var called = false;
        var result = Result.Success(4);

        // Act
        Result<int> tapped = await result.TapErrorAsync((_, _) =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        tapped.IsSuccess.ShouldBeTrue();
        tapped.Value.ShouldBe(4);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task TapErrorAsync_WithToken_WhenDefault_ReturnsDefaultWithoutCallingEffect()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act
        Result<int> tapped = await result.TapErrorAsync((_, _) =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        tapped.IsSuccess.ShouldBeFalse();
        tapped.IsFailure.ShouldBeFalse();
        called.ShouldBeFalse();
    }

    #endregion

    #region EnsureAsync

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task EnsureAsync_WithToken_WhenSuccess_AppliesPredicateAndPassesToken(bool yield, bool holds)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var error = new OtherError();
        var result = Result.Success(8);

        // Act
        Result<int> ensured = await result.EnsureAsync(async (v, ct) =>
        {
            seen = ct;
            return await LaterValue(yield, v == 8 && holds);
        }, error, cts.Token);

        // Assert
        seen.ShouldBe(cts.Token);
        ensured.IsSuccess.ShouldBe(holds);
        if (holds)
            ensured.Value.ShouldBe(8);
        else
            ensured.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task EnsureAsync_WithToken_WhenFailure_PropagatesOriginalErrorWithoutCallingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        Result<int> ensured = await result.EnsureAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(false);
        }, new OtherError());

        // Assert
        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBeSameAs(original);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task EnsureAsync_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingPredicate()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result<int>>> act = async () => await result.EnsureAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(true);
        }, new TestError());
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    #endregion

    #region MapErrorAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapErrorAsync_WithToken_WhenFailure_ReturnsMappedErrorAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        IError? seenError = null;
        var original = new TestError();
        IError replacement = new OtherError();
        var result = Result.Failure<int>(original);

        // Act
        Result<int> mapped = await result.MapErrorAsync(async (e, ct) =>
        {
            seenError = e;
            seenToken = ct;
            return await LaterValue(yield, replacement);
        }, cts.Token);

        // Assert
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBeSameAs(replacement);
        seenError.ShouldBeSameAs(original);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task MapErrorAsync_WithToken_WhenSuccess_ReturnsSuccessWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        var result = Result.Success(4);

        // Act
        Result<int> mapped = await result.MapErrorAsync((e, _) =>
        {
            called = true;
            return ValueTask.FromResult(e);
        });

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(4);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task MapErrorAsync_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingMapper()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result<int>>> act = async () => await result.MapErrorAsync((e, _) =>
        {
            called = true;
            return ValueTask.FromResult(e);
        });
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    #endregion

    #region CompensateAsync

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompensateAsync_WithToken_WhenFailure_ReturnsFallbackResultAndPassesToken(bool yield)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        IError? seenError = null;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        Result<int> compensated = await result.CompensateAsync(async (e, ct) =>
        {
            seenError = e;
            seenToken = ct;
            return await LaterValue(yield, Result.Success(0));
        }, cts.Token);

        // Assert
        compensated.IsSuccess.ShouldBeTrue();
        compensated.Value.ShouldBe(0);
        seenError.ShouldBeSameAs(original);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task CompensateAsync_WithToken_WhenSuccess_ReturnsSuccessWithoutCallingFallback()
    {
        // Arrange
        var called = false;
        var result = Result.Success(4);

        // Act
        Result<int> compensated = await result.CompensateAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(Result.Success(0));
        });

        // Assert
        compensated.IsSuccess.ShouldBeTrue();
        compensated.Value.ShouldBe(4);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task CompensateAsync_WithToken_WhenDefault_ThrowsResultExceptionWithoutCallingFallback()
    {
        // Arrange
        var called = false;
        Result<int> result = default;

        // Act + Assert
        Func<Task<Result<int>>> act = async () => await result.CompensateAsync((_, _) =>
        {
            called = true;
            return ValueTask.FromResult(Result.Success(0));
        });
        await Should.ThrowAsync<ResultException>(act);
        called.ShouldBeFalse();
    }

    #endregion

    #region FinallyAsync

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FinallyAsync_WithToken_OnEitherRail_AwaitsActionWithSameResultAndToken(bool yield, bool success)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken seenToken = default;
        Result<int>? seen = null;
        var result = success ? Result.Success(1) : Result.Failure<int>(new TestError());

        // Act
        Result<int> returned = await result.FinallyAsync(async (r, ct) =>
        {
            await LaterValue(yield, 0);
            seen = r;
            seenToken = ct;
        }, cts.Token);

        // Assert
        returned.ShouldBe(result);
        returned.IsSuccess.ShouldBe(success);
        seen.ShouldBe(result);
        seenToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task FinallyAsync_WithToken_WhenDefault_CallsActionWithDefault()
    {
        // Arrange
        Result<int>? seen = null;
        Result<int> result = default;

        // Act
        Result<int> returned = await result.FinallyAsync((r, _) =>
        {
            seen = r;
            return ValueTask.CompletedTask;
        });

        // Assert
        returned.IsSuccess.ShouldBeFalse();
        returned.IsFailure.ShouldBeFalse();
        seen.ShouldBe(default(Result<int>));
    }

    #endregion

    #region Overload binding

    [Fact]
    public async Task MapAsync_WithPortReturningValueTaskAndTakingToken_BindsToTokenOverload()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var port = new RecordingPort();
        var result = Result.Success(3);

        // Act — a ValueTask-returning method group with a token compiles against the token overload only
        Result<int> mapped = await result.MapAsync(port.DoubleAsync, cts.Token);

        // Assert
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(6);
        port.Tokens.ShouldBe([cts.Token]);
    }

    [Fact]
    public async Task BindAsync_WithPortLambdaTakingToken_BindsToTokenOverload()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var port = new RecordingPort();
        var result = Result.Success(3);

        // Act — the usage guide's token form (docs/guides/usage.md, Compose asynchronously)
        Result<string> bound = await result.BindAsync((v, ct) => port.FindAsync(v, ct), cts.Token);

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe("item-3");
        port.Tokens.ShouldBe([cts.Token]);
    }

    [Fact]
    public async Task BindAsync_WithAsyncTwoParameterLambda_BindsToTokenOverload()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var port = new RecordingPort();
        var result = Result.Success(3);

        // Act — async (v, ct) => … awaits a ValueTask port with the token: only the token overload has ct
        Result<string> bound = await result.BindAsync(async (v, ct) => await port.FindAsync(v, ct), cts.Token);

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        port.Tokens.ShouldBe([cts.Token]);
    }

    [Fact]
    public async Task MapAsync_WithAsyncOneParameterLambda_BindsToTaskOverload()
    {
        // Arrange
        var result = Result.Success(3);
        Func<int, Task<int>> taskOnly = async v =>
        {
            await Task.Yield();
            return v + 1;
        };

        // Act — async v => … compiles (no CS0121), so it binds to the only one-parameter overload: the Task one,
        // the same overload a Func<int, Task<int>> binds to
        Result<int> fromLambda = await result.MapAsync(async v =>
        {
            await Task.Yield();
            return v + 1;
        });
        Result<int> fromTaskDelegate = await result.MapAsync(taskOnly);

        // Assert
        fromLambda.IsSuccess.ShouldBeTrue();
        fromLambda.Value.ShouldBe(4);
        fromLambda.ShouldBe(fromTaskDelegate);
    }

    [Fact]
    public async Task EveryOperation_WithTaskMethodGroupsAndValueTaskMethodGroups_BindsWithoutAmbiguity()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var port = new RecordingPort();
        var success = Result.Success(2);
        var failure = Result.Failure<int>(new TestError());

        // Act — each token overload receives a method group that only matches it (ValueTask + CancellationToken)
        Result<int> tapped = await success.TapAsync(port.TouchAsync, cts.Token);
        Result<int> ensured = await tapped.EnsureAsync(port.IsPositiveAsync, new OtherError(), cts.Token);
        Result<int> tappedError = await failure.TapErrorAsync(port.LogAsync, cts.Token);
        Result<int> compensated = await tappedError.CompensateAsync(port.RecoverAsync, cts.Token);
        Result<int> finalized = await compensated.FinallyAsync(port.AuditAsync, cts.Token);

        // Assert
        ensured.IsSuccess.ShouldBeTrue();
        ensured.Value.ShouldBe(2);
        finalized.IsSuccess.ShouldBeTrue();
        finalized.Value.ShouldBe(0);
        port.Tokens.Count.ShouldBe(5);
        port.Tokens.ShouldAllBe(t => t == cts.Token);
    }

    #endregion

    #region Cross-cutting: null guards, default token, cancellation

    [Theory]
    [MemberData(nameof(NullArgumentNames))]
    public void TokenOverload_WithNullArgument_ThrowsArgumentNullExceptionAtTheCall(string argument)
    {
        // Arrange
        Task? pending = null;

        // Act
        var thrown = Record.Exception(() =>
        {
            pending = CallTokenFormWithNull(argument);
        });

        // Assert
        pending.ShouldBeNull();
        thrown.ShouldBeOfType<ArgumentNullException>().ParamName.ShouldBe(argument[(argument.IndexOf('.', StringComparison.Ordinal) + 1)..]);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TokenOverload_WithoutTokenArgument_PassesCancellationTokenNone(string operation)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var seen = cts.Token;

        // Act
        var outcome = await InvokeTokenForm(operation, ct =>
        {
            seen = ct;
            return ValueTask.CompletedTask;
        }, ct: null);

        // Assert
        outcome.ShouldBeTrue();
        seen.ShouldBe(CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TokenOverload_WhenTokenAlreadyCanceled_StillInvokesDelegate(string operation)
    {
        // Arrange — the extension neither checks the token nor short-circuits on it (ADR-RESULT-001 D8)
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var sawCanceled = false;

        // Act
        var outcome = await InvokeTokenForm(operation, ct =>
        {
            sawCanceled = ct.IsCancellationRequested;
            return ValueTask.CompletedTask;
        }, cts.Token);

        // Assert
        outcome.ShouldBeTrue();
        sawCanceled.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TokenOverload_WhenDelegateThrowsOperationCanceledException_Propagates(string operation)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        // Record.ExceptionAsync awaits the task itself: Should.ThrowAsync reports a canceled task as a new
        // TaskCanceledException, not as the exception the delegate threw.
        var thrown = await Record.ExceptionAsync(async () =>
            await InvokeTokenForm(operation, async ct =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
            }, cts.Token));

        // Assert
        thrown.ShouldBeOfType<OperationCanceledException>().CancellationToken.ShouldBe(cts.Token);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TokenOverload_WhenDelegateThrows_PropagatesSameException(string operation)
    {
        // Arrange
        var boom = new InvalidOperationException("boom");

        // Act
        var thrown = await Record.ExceptionAsync(async () =>
            await InvokeTokenForm(operation, async _ =>
            {
                await Task.Yield();
                throw boom;
            }, CancellationToken.None));

        // Assert
        thrown.ShouldBeSameAs(boom);
    }

    #endregion

    #region Helpers

    // Runs the token overload of the operation on a result whose rail invokes the delegate (a failure for TapError,
    // MapError and Compensate; a success otherwise). ct == null omits the ct argument. Returns true when the outcome
    // keeps the expected rail.
    private static async Task<bool> InvokeTokenForm(string operation, Func<CancellationToken, ValueTask> body, CancellationToken? ct)
    {
        var success = Result.Success(1);
        var failure = Result.Failure<int>(new TestError());
        var token = ct.GetValueOrDefault();
        var omit = ct is null;

        async ValueTask<int> Mapper(int v, CancellationToken c)
        {
            await body(c);
            return v;
        }

        async ValueTask<Result<int>> Binder(int v, CancellationToken c)
        {
            await body(c);
            return Result.Success(v);
        }

        async ValueTask<Result> CommandBinder(int v, CancellationToken c)
        {
            await body(c);
            return Result.Success();
        }

        async ValueTask<bool> OnSuccess(int v, CancellationToken c)
        {
            await body(c);
            return true;
        }

        async ValueTask<bool> Predicate(int v, CancellationToken c)
        {
            await body(c);
            return true;
        }

        async ValueTask<IError> ErrorMapper(IError e, CancellationToken c)
        {
            await body(c);
            return e;
        }

        async ValueTask<Result<int>> Fallback(IError e, CancellationToken c)
        {
            await body(c);
            return Result.Success(0);
        }

        ValueTask Effect(int v, CancellationToken c) => body(c);
        ValueTask ErrorEffect(IError e, CancellationToken c) => body(c);
        ValueTask Final(Result<int> r, CancellationToken c) => body(c);
        ValueTask<bool> OnFailure(IError e, CancellationToken c) => ValueTask.FromResult(false);

        return operation switch
        {
            "Map" => (omit ? await success.MapAsync(Mapper) : await success.MapAsync(Mapper, token)).IsSuccess,
            "Bind" => (omit ? await success.BindAsync(Binder) : await success.BindAsync(Binder, token)).IsSuccess,
            "BindToCommand" => (omit ? await success.BindAsync(CommandBinder) : await success.BindAsync(CommandBinder, token)).IsSuccess,
            "Match" => omit ? await success.MatchAsync(OnSuccess, OnFailure) : await success.MatchAsync(OnSuccess, OnFailure, token),
            "Tap" => (omit ? await success.TapAsync(Effect) : await success.TapAsync(Effect, token)).IsSuccess,
            "TapError" => (omit ? await failure.TapErrorAsync(ErrorEffect) : await failure.TapErrorAsync(ErrorEffect, token)).IsFailure,
            "Ensure" => (omit ? await success.EnsureAsync(Predicate, new OtherError()) : await success.EnsureAsync(Predicate, new OtherError(), token)).IsSuccess,
            "MapError" => (omit ? await failure.MapErrorAsync(ErrorMapper) : await failure.MapErrorAsync(ErrorMapper, token)).IsFailure,
            "Compensate" => (omit ? await failure.CompensateAsync(Fallback) : await failure.CompensateAsync(Fallback, token)).IsSuccess,
            "Finally" => (omit ? await success.FinallyAsync(Final) : await success.FinallyAsync(Final, token)).IsSuccess,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };
    }

    private static Task CallTokenFormWithNull(string argument)
    {
        var result = Result.Success(1);
        var ct = CancellationToken.None;
        return argument switch
        {
            "Map.mapper" => result.MapAsync((Func<int, CancellationToken, ValueTask<int>>)null!, ct).AsTask(),
            "Bind.binder" => result.BindAsync((Func<int, CancellationToken, ValueTask<Result<int>>>)null!, ct).AsTask(),
            "BindToCommand.binder" => result.BindAsync((Func<int, CancellationToken, ValueTask<Result>>)null!, ct).AsTask(),
            "Match.onSuccess" => result.MatchAsync(null!, (_, _) => ValueTask.FromResult(0), ct).AsTask(),
            "Match.onFailure" => result.MatchAsync((v, _) => ValueTask.FromResult(v), null!, ct).AsTask(),
            "Tap.action" => result.TapAsync((Func<int, CancellationToken, ValueTask>)null!, ct).AsTask(),
            "TapError.action" => result.TapErrorAsync((Func<IError, CancellationToken, ValueTask>)null!, ct).AsTask(),
            "Ensure.predicate" => result.EnsureAsync((Func<int, CancellationToken, ValueTask<bool>>)null!, new TestError(), ct).AsTask(),
            "Ensure.error" => result.EnsureAsync((_, _) => ValueTask.FromResult(true), null!, ct).AsTask(),
            "MapError.mapper" => result.MapErrorAsync((Func<IError, CancellationToken, ValueTask<IError>>)null!, ct).AsTask(),
            "Compensate.fallback" => result.CompensateAsync((Func<IError, CancellationToken, ValueTask<Result<int>>>)null!, ct).AsTask(),
            "Finally.action" => result.FinallyAsync((Func<Result<int>, CancellationToken, ValueTask>)null!, ct).AsTask(),
            _ => throw new ArgumentOutOfRangeException(nameof(argument), argument, "Unknown argument."),
        };
    }

    private static async Task<T> Later<T>(bool yield, T value)
    {
        if (yield)
            await Task.Yield();
        return value;
    }

    private static async ValueTask<T> LaterValue<T>(bool yield, T value)
    {
        if (yield)
            await Task.Yield();
        return value;
    }

    // A module port: ValueTask-returning, CancellationToken last.
    private sealed class RecordingPort
    {
        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask<int> DoubleAsync(int value, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            return ValueTask.FromResult(value * 2);
        }

        public async ValueTask<Result<string>> FindAsync(int id, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            await Task.Yield();
            return Result.Success($"item-{id}");
        }

        public ValueTask TouchAsync(int value, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsPositiveAsync(int value, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            return ValueTask.FromResult(value > 0);
        }

        public ValueTask LogAsync(IError error, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            return ValueTask.CompletedTask;
        }

        public ValueTask<Result<int>> RecoverAsync(IError error, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            return ValueTask.FromResult(Result.Success(0));
        }

        public ValueTask AuditAsync(Result<int> result, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
