namespace MicroKit.Result;

// Async operations of the non-generic Result (ADR-RESULT-001 D9), in both delegate forms of D8: a Task-returning
// delegate without a token, and a CancellationToken-taking delegate returning a ValueTask.
public static partial class AsyncResultExtensions
{
    /// <summary>Asynchronously maps a successful non-generic result to a <see cref="Result{T}"/> holding the value the mapper returns.</summary>
    /// <typeparam name="TOut">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="mapper">Produces the value on success.</param>
    /// <returns>A result holding the value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static ValueTask<Result<TOut>> MapAsync<TOut>(this Result result, Func<Task<TOut>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper);

        static async ValueTask<Result<TOut>> Core(Result result, Func<Task<TOut>> mapper)
        {
            return result.IsSuccess
                ? Result<TOut>.Success(await mapper().ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously maps a successful non-generic result to a <see cref="Result{T}"/> holding the value the mapper returns, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TOut">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="mapper">Produces the value on success.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>A result holding the value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static ValueTask<Result<TOut>> MapAsync<TOut>(this Result result, Func<CancellationToken, ValueTask<TOut>> mapper, CancellationToken ct = default)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper, ct);

        static async ValueTask<Result<TOut>> Core(Result result, Func<CancellationToken, ValueTask<TOut>> mapper, CancellationToken ct)
        {
            return result.IsSuccess
                ? Result<TOut>.Success(await mapper(ct).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a command onto a successful non-generic result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="binder">The command to run on success.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result> BindAsync(this Result result, Func<Task<Result>> binder)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder);

        static async ValueTask<Result> Core(Result result, Func<Task<Result>> binder)
        {
            return result.IsSuccess
                ? await binder().ConfigureAwait(false)
                : Result.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a command onto a successful non-generic result, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="binder">The command to run on success.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result> BindAsync(this Result result, Func<CancellationToken, ValueTask<Result>> binder, CancellationToken ct = default)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder, ct);

        static async ValueTask<Result> Core(Result result, Func<CancellationToken, ValueTask<Result>> binder, CancellationToken ct)
        {
            return result.IsSuccess
                ? await binder(ct).ConfigureAwait(false)
                : Result.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a value-producing operation onto a successful non-generic result.</summary>
    /// <typeparam name="TOut">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="binder">The operation to run on success.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result<TOut>> BindAsync<TOut>(this Result result, Func<Task<Result<TOut>>> binder)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder);

        static async ValueTask<Result<TOut>> Core(Result result, Func<Task<Result<TOut>>> binder)
        {
            return result.IsSuccess
                ? await binder().ConfigureAwait(false)
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a value-producing operation onto a successful non-generic result, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TOut">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="binder">The operation to run on success.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result<TOut>> BindAsync<TOut>(this Result result, Func<CancellationToken, ValueTask<Result<TOut>>> binder, CancellationToken ct = default)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder, ct);

        static async ValueTask<Result<TOut>> Core(Result result, Func<CancellationToken, ValueTask<Result<TOut>>> binder, CancellationToken ct)
        {
            return result.IsSuccess
                ? await binder(ct).ConfigureAwait(false)
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously matches success or failure of a non-generic result.</summary>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static ValueTask<TOut> MatchAsync<TOut>(this Result result, Func<Task<TOut>> onSuccess, Func<IError, Task<TOut>> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return Core(result, onSuccess, onFailure);

        static async ValueTask<TOut> Core(Result result, Func<Task<TOut>> onSuccess, Func<IError, Task<TOut>> onFailure)
        {
            return result.IsSuccess
                ? await onSuccess().ConfigureAwait(false)
                : await onFailure(result.Error).ConfigureAwait(false);
        }
    }

    /// <summary>Asynchronously matches success or failure of a non-generic result, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The value produced by the matching function.</returns>
    public static ValueTask<TOut> MatchAsync<TOut>(this Result result, Func<CancellationToken, ValueTask<TOut>> onSuccess, Func<IError, CancellationToken, ValueTask<TOut>> onFailure, CancellationToken ct = default)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return Core(result, onSuccess, onFailure, ct);

        static async ValueTask<TOut> Core(Result result, Func<CancellationToken, ValueTask<TOut>> onSuccess, Func<IError, CancellationToken, ValueTask<TOut>> onFailure, CancellationToken ct)
        {
            return result.IsSuccess
                ? await onSuccess(ct).ConfigureAwait(false)
                : await onFailure(result.Error, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Asynchronously executes a side effect when a non-generic result is successful.</summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side effect.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> TapAsync(this Result result, Func<Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(result, action);

        static async ValueTask<Result> Core(Result result, Func<Task> action)
        {
            if (result.IsSuccess)
                await action().ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously executes a side effect when a non-generic result is successful, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side effect.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> TapAsync(this Result result, Func<CancellationToken, ValueTask> action, CancellationToken ct = default)
    {
        ResultGuard.NotNull(action);
        return Core(result, action, ct);

        static async ValueTask<Result> Core(Result result, Func<CancellationToken, ValueTask> action, CancellationToken ct)
        {
            if (result.IsSuccess)
                await action(ct).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously executes a side effect on the error of a failed non-generic result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side effect.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> TapErrorAsync(this Result result, Func<IError, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(result, action);

        static async ValueTask<Result> Core(Result result, Func<IError, Task> action)
        {
            if (result.IsFailure)
                await action(result.Error).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously executes a side effect on the error of a failed non-generic result, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side effect.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> TapErrorAsync(this Result result, Func<IError, CancellationToken, ValueTask> action, CancellationToken ct = default)
    {
        ResultGuard.NotNull(action);
        return Core(result, action, ct);

        static async ValueTask<Result> Core(Result result, Func<IError, CancellationToken, ValueTask> action, CancellationToken ct)
        {
            if (result.IsFailure)
                await action(result.Error, ct).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously turns a successful non-generic result into a failure when the predicate returns <see langword="false"/>.</summary>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The condition a success must meet.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static ValueTask<Result> EnsureAsync(this Result result, Func<Task<bool>> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(result, predicate, error);

        static async ValueTask<Result> Core(Result result, Func<Task<bool>> predicate, IError error)
        {
            if (!result.IsSuccess)
                return Result.Failure(result.Error);

            return await predicate().ConfigureAwait(false) ? result : Result.Failure(error);
        }
    }

    /// <summary>Asynchronously turns a successful non-generic result into a failure when the predicate returns <see langword="false"/>, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The condition a success must meet.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static ValueTask<Result> EnsureAsync(this Result result, Func<CancellationToken, ValueTask<bool>> predicate, IError error, CancellationToken ct = default)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(result, predicate, error, ct);

        static async ValueTask<Result> Core(Result result, Func<CancellationToken, ValueTask<bool>> predicate, IError error, CancellationToken ct)
        {
            if (!result.IsSuccess)
                return Result.Failure(result.Error);

            return await predicate(ct).ConfigureAwait(false) ? result : Result.Failure(error);
        }
    }

    /// <summary>Asynchronously transforms the error of a failed non-generic result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The async error transformation function.</param>
    /// <returns>The original result if successful, or a failure with the mapped error.</returns>
    public static ValueTask<Result> MapErrorAsync(this Result result, Func<IError, Task<IError>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper);

        static async ValueTask<Result> Core(Result result, Func<IError, Task<IError>> mapper)
        {
            return result.IsSuccess
                ? result
                : Result.Failure(await mapper(result.Error).ConfigureAwait(false)); // Error throws on a default result
        }
    }

    /// <summary>Asynchronously transforms the error of a failed non-generic result, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The async error transformation function.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result if successful, or a failure with the mapped error.</returns>
    public static ValueTask<Result> MapErrorAsync(this Result result, Func<IError, CancellationToken, ValueTask<IError>> mapper, CancellationToken ct = default)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper, ct);

        static async ValueTask<Result> Core(Result result, Func<IError, CancellationToken, ValueTask<IError>> mapper, CancellationToken ct)
        {
            return result.IsSuccess
                ? result
                : Result.Failure(await mapper(result.Error, ct).ConfigureAwait(false)); // Error throws on a default result
        }
    }

    /// <summary>Asynchronously provides a fallback when a non-generic result is a failure.</summary>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The async fallback, receiving the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static ValueTask<Result> CompensateAsync(this Result result, Func<IError, Task<Result>> fallback)
    {
        ResultGuard.NotNull(fallback);
        return Core(result, fallback);

        static async ValueTask<Result> Core(Result result, Func<IError, Task<Result>> fallback)
        {
            return result.IsSuccess
                ? result
                : await fallback(result.Error).ConfigureAwait(false); // Error throws on a default result
        }
    }

    /// <summary>Asynchronously provides a fallback when a non-generic result is a failure, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The async fallback, receiving the original error.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static ValueTask<Result> CompensateAsync(this Result result, Func<IError, CancellationToken, ValueTask<Result>> fallback, CancellationToken ct = default)
    {
        ResultGuard.NotNull(fallback);
        return Core(result, fallback, ct);

        static async ValueTask<Result> Core(Result result, Func<IError, CancellationToken, ValueTask<Result>> fallback, CancellationToken ct)
        {
            return result.IsSuccess
                ? result
                : await fallback(result.Error, ct).ConfigureAwait(false); // Error throws on a default result
        }
    }

    /// <summary>Asynchronously executes an action on a non-generic result, whatever its outcome.</summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> FinallyAsync(this Result result, Func<Result, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(result, action);

        static async ValueTask<Result> Core(Result result, Func<Result, Task> action)
        {
            await action(result).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously executes an action on a non-generic result, whatever its outcome, passing <paramref name="ct"/> to the delegate.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action, receiving the full result.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> FinallyAsync(this Result result, Func<Result, CancellationToken, ValueTask> action, CancellationToken ct = default)
    {
        ResultGuard.NotNull(action);
        return Core(result, action, ct);

        static async ValueTask<Result> Core(Result result, Func<Result, CancellationToken, ValueTask> action, CancellationToken ct)
        {
            await action(result, ct).ConfigureAwait(false);
            return result;
        }
    }
}
