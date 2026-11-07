namespace MicroKit.Result;

// Non-generic Result: the operations Result<T> has (ADR-RESULT-001 D9), and Bind from a value to a command.
public static partial class ResultExtensions
{
    /// <summary>Maps a successful non-generic result to a <see cref="Result{T}"/> holding the value <paramref name="mapper"/> returns. A failure is propagated unchanged.</summary>
    /// <typeparam name="TOut">The value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="mapper">Produces the value on success.</param>
    /// <returns>A result holding the value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static Result<TOut> Map<TOut>(this Result result, Func<TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        return result.IsSuccess
            ? Result<TOut>.Success(mapper())
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Chains a result-producing command onto a successful non-generic result. A failure is propagated unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The command to run on success.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static Result Bind(this Result result, Func<Result> binder)
    {
        ResultGuard.NotNull(binder);
        return result.IsSuccess
            ? binder()
            : Result.Failure(result.Error);
    }

    /// <summary>Chains a value-producing operation onto a successful non-generic result. A failure is propagated unchanged.</summary>
    /// <typeparam name="TOut">The value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The operation to run on success.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static Result<TOut> Bind<TOut>(this Result result, Func<Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        return result.IsSuccess
            ? binder()
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Chains a command onto the value of a successful result, dropping the value. A failure is propagated unchanged.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The command to run with the value.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static Result Bind<TIn>(this Result<TIn> result, Func<TIn, Result> binder)
    {
        ResultGuard.NotNull(binder);
        return result.IsSuccess
            ? binder(result.Value)
            : Result.Failure(result.Error);
    }

    /// <summary>Turns a successful non-generic result into a failure with <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The condition a success must meet.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static Result Ensure(this Result result, Func<bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        if (!result.IsSuccess)
            return Result.Failure(result.Error);

        return predicate() ? result : Result.Failure(error);
    }

    /// <summary>Transforms the error of a failed non-generic result. A success is returned unchanged.</summary>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The error transformation function.</param>
    /// <returns>The original result if successful, or a failure with the mapped error.</returns>
    public static Result MapError(this Result result, Func<IError, IError> mapper)
    {
        ResultGuard.NotNull(mapper);
        // !IsSuccess, not IsFailure: Error throws ResultException on a default result
        return result.IsSuccess
            ? result
            : Result.Failure(mapper(result.Error));
    }

    /// <summary>Provides a fallback result when the non-generic result is a failure. A success is returned unchanged.</summary>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The fallback, receiving the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static Result Compensate(this Result result, Func<IError, Result> fallback)
    {
        ResultGuard.NotNull(fallback);
        // !IsSuccess, not IsFailure: Error throws ResultException on a default result
        return result.IsSuccess
            ? result
            : fallback(result.Error);
    }
}
