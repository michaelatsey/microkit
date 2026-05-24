namespace MicroKit.Result;

/// <summary>
/// Synchronous extension methods for <see cref="Result{T}"/> and <see cref="Result"/>.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Maps the value of a successful result using the specified selector.
    /// If the result is a failure, the error is propagated unchanged.
    /// </summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="mapper">The transformation function.</param>
    /// <returns>A new result with the mapped value, or the original failure.</returns>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        return result.IsSuccess
            ? Result<TOut>.Success(mapper(result.Value))
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>
    /// Chains a result-producing function onto a successful result.
    /// If the result is a failure, the error is propagated unchanged.
    /// </summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        return result.IsSuccess
            ? binder(result.Value)
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>
    /// Matches the result, executing one of two functions based on success or failure.
    /// </summary>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="result">The result to match.</param>
    /// <param name="onSuccess">Executed when the result is successful.</param>
    /// <param name="onFailure">Executed when the result is a failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<IError, TOut> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Error);
    }

    /// <summary>
    /// Executes a side-effect action on the value if the result is successful.
    /// Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsSuccess)
            action(result.Value);
        return result;
    }

    /// <summary>
    /// Executes a side-effect action on the error if the result is a failure.
    /// Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static Result<T> TapError<T>(this Result<T> result, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsFailure)
            action(result.Error);
        return result;
    }

    /// <summary>
    /// Validates the value against a predicate. If the predicate returns false,
    /// the result becomes a failure with the specified error.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The validation predicate.</param>
    /// <param name="error">The error to use if the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);

        if (result.IsFailure)
            return result;

        return predicate(result.Value)
            ? result
            : Result<T>.Failure(error);
    }

    /// <summary>
    /// Matches a non-generic result, executing one of two functions.
    /// </summary>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="result">The result to match.</param>
    /// <param name="onSuccess">Executed when the result is successful.</param>
    /// <param name="onFailure">Executed when the result is a failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static TOut Match<TOut>(
        this Result result,
        Func<TOut> onSuccess,
        Func<IError, TOut> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return result.IsSuccess
            ? onSuccess()
            : onFailure(result.Error);
    }

    /// <summary>
    /// Executes a side-effect action if the non-generic result is successful.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static Result Tap(this Result result, Action action)
    {
        ResultGuard.NotNull(action);
        if (result.IsSuccess)
            action();
        return result;
    }

    /// <summary>
    /// Executes a side-effect action on the error if the non-generic result is a failure.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static Result TapError(this Result result, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsFailure)
            action(result.Error);
        return result;
    }
}
