namespace MicroKit.Result;

// Overloads whose delegate takes a CancellationToken and returns a ValueTask (ADR-RESULT-001 D8), on Result<T>,
// and BindAsync from a value to a command (D9). Two same-name overloads that differ only by a Task or ValueTask
// delegate would make every async lambda ambiguous (CS0121): arity separates them here.
public static partial class AsyncResultExtensions
{
    /// <summary>Asynchronously maps the value of a successful result, passing <paramref name="ct"/> to the mapper.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="mapper">The async transformation function.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static ValueTask<Result<TOut>> MapAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, CancellationToken, ValueTask<TOut>> mapper, CancellationToken ct = default)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper, ct);

        static async ValueTask<Result<TOut>> Core(Result<TIn> result, Func<TIn, CancellationToken, ValueTask<TOut>> mapper, CancellationToken ct)
        {
            return result.IsSuccess
                ? Result<TOut>.Success(await mapper(result.Value, ct).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a result-producing function, passing <paramref name="ct"/> to it.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The async function producing a new result.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result<TOut>> BindAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, CancellationToken, ValueTask<Result<TOut>>> binder, CancellationToken ct = default)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder, ct);

        static async ValueTask<Result<TOut>> Core(Result<TIn> result, Func<TIn, CancellationToken, ValueTask<Result<TOut>>> binder, CancellationToken ct)
        {
            return result.IsSuccess
                ? await binder(result.Value, ct).ConfigureAwait(false)
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a command onto the value of a successful result, dropping the value.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The async command to run with the value.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result> BindAsync<TIn>(this Result<TIn> result, Func<TIn, Task<Result>> binder)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder);

        static async ValueTask<Result> Core(Result<TIn> result, Func<TIn, Task<Result>> binder)
        {
            return result.IsSuccess
                ? await binder(result.Value).ConfigureAwait(false)
                : Result.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously chains a command onto the value of a successful result, passing <paramref name="ct"/> to it and dropping the value.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The async command to run with the value.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result> BindAsync<TIn>(this Result<TIn> result, Func<TIn, CancellationToken, ValueTask<Result>> binder, CancellationToken ct = default)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder, ct);

        static async ValueTask<Result> Core(Result<TIn> result, Func<TIn, CancellationToken, ValueTask<Result>> binder, CancellationToken ct)
        {
            return result.IsSuccess
                ? await binder(result.Value, ct).ConfigureAwait(false)
                : Result.Failure(result.Error);
        }
    }

    /// <summary>Asynchronously matches success or failure, passing <paramref name="ct"/> to the function that runs.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="result">The result to match.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The value produced by the matching function.</returns>
    public static ValueTask<TOut> MatchAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, CancellationToken, ValueTask<TOut>> onSuccess, Func<IError, CancellationToken, ValueTask<TOut>> onFailure, CancellationToken ct = default)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return Core(result, onSuccess, onFailure, ct);

        static async ValueTask<TOut> Core(Result<TIn> result, Func<TIn, CancellationToken, ValueTask<TOut>> onSuccess, Func<IError, CancellationToken, ValueTask<TOut>> onFailure, CancellationToken ct)
        {
            return result.IsSuccess
                ? await onSuccess(result.Value, ct).ConfigureAwait(false)
                : await onFailure(result.Error, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Asynchronously executes a side effect on the value of a successful result, passing <paramref name="ct"/> to it.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side effect.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapAsync<T>(this Result<T> result, Func<T, CancellationToken, ValueTask> action, CancellationToken ct = default)
    {
        ResultGuard.NotNull(action);
        return Core(result, action, ct);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<T, CancellationToken, ValueTask> action, CancellationToken ct)
        {
            if (result.IsSuccess)
                await action(result.Value, ct).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously executes a side effect on the error of a failed result, passing <paramref name="ct"/> to it.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side effect.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapErrorAsync<T>(this Result<T> result, Func<IError, CancellationToken, ValueTask> action, CancellationToken ct = default)
    {
        ResultGuard.NotNull(action);
        return Core(result, action, ct);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<IError, CancellationToken, ValueTask> action, CancellationToken ct)
        {
            if (result.IsFailure)
                await action(result.Error, ct).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Asynchronously validates the value against a predicate, passing <paramref name="ct"/> to it.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The async validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static ValueTask<Result<T>> EnsureAsync<T>(this Result<T> result, Func<T, CancellationToken, ValueTask<bool>> predicate, IError error, CancellationToken ct = default)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(result, predicate, error, ct);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<T, CancellationToken, ValueTask<bool>> predicate, IError error, CancellationToken ct)
        {
            if (result.IsFailure)
                return result;

            return await predicate(result.Value, ct).ConfigureAwait(false)
                ? result
                : Result<T>.Failure(error);
        }
    }

    /// <summary>Asynchronously transforms the error of a failed result, passing <paramref name="ct"/> to the mapper.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The async error transformation function.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result if successful, or a failure with the mapped error.</returns>
    public static ValueTask<Result<T>> MapErrorAsync<T>(this Result<T> result, Func<IError, CancellationToken, ValueTask<IError>> mapper, CancellationToken ct = default)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper, ct);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<IError, CancellationToken, ValueTask<IError>> mapper, CancellationToken ct)
        {
            return result.IsSuccess
                ? result
                : Result<T>.Failure(await mapper(result.Error, ct).ConfigureAwait(false)); // Error throws on a default result
        }
    }

    /// <summary>Asynchronously provides a fallback result on failure, passing <paramref name="ct"/> to the fallback.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The async fallback, receiving the original error.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static ValueTask<Result<T>> CompensateAsync<T>(this Result<T> result, Func<IError, CancellationToken, ValueTask<Result<T>>> fallback, CancellationToken ct = default)
    {
        ResultGuard.NotNull(fallback);
        return Core(result, fallback, ct);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<IError, CancellationToken, ValueTask<Result<T>>> fallback, CancellationToken ct)
        {
            return result.IsSuccess
                ? result
                : await fallback(result.Error, ct).ConfigureAwait(false); // Error throws on a default result
        }
    }

    /// <summary>Asynchronously executes an action whatever the outcome, passing <paramref name="ct"/> to it.</summary>
    /// <remarks>The delegate receives <paramref name="ct"/> and returns a <see cref="ValueTask"/>, as a module port does (ADR-RESULT-001 D8).</remarks>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action, receiving the full result.</param>
    /// <param name="ct">The token passed to the delegate. The extension neither checks it nor catches a cancellation: it propagates. Omitted, the delegate receives <see cref="CancellationToken.None"/>: pass the caller's token (CA2016 reports a missing one).</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> FinallyAsync<T>(this Result<T> result, Func<Result<T>, CancellationToken, ValueTask> action, CancellationToken ct = default)
    {
        ResultGuard.NotNull(action);
        return Core(result, action, ct);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<Result<T>, CancellationToken, ValueTask> action, CancellationToken ct)
        {
            await action(result, ct).ConfigureAwait(false);
            return result;
        }
    }
}
