namespace MicroKit.Result;

/// <summary>
/// Synchronous extension methods for <see cref="Result{T}"/> and <see cref="Result"/>.
/// </summary>
public static class ResultExtensions
{
    #region Map

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

    #endregion

    #region Bind

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

    #endregion

    #region Match

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

    #endregion

    #region Tap

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

    #endregion

    #region TapError

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

    #endregion

    #region Ensure

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

    #endregion

    #region MapError

    /// <summary>
    /// Transforms the error of a failed result using the specified mapper.
    /// If the result is a success, it is returned unchanged.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static Result<T> MapError<T>(this Result<T> result, Func<IError, IError> mapper)
    {
        ResultGuard.NotNull(mapper);
        return result.IsFailure
            ? Result<T>.Failure(mapper(result.Error))
            : result;
    }

    #endregion

    #region Compensate

    /// <summary>
    /// Provides a fallback <see cref="Result{T}"/> when the result is a failure.
    /// If the result is a success, it is returned unchanged.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static Result<T> Compensate<T>(this Result<T> result, Func<IError, Result<T>> fallback)
    {
        ResultGuard.NotNull(fallback);
        return result.IsFailure
            ? fallback(result.Error)
            : result;
    }

    #endregion

    #region ValueAccess

    /// <summary>
    /// Returns the success value, or <paramref name="defaultValue"/> if the result is a failure.
    /// Never throws.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="defaultValue">The fallback value returned on failure.</param>
    /// <returns>The success value, or <paramref name="defaultValue"/>.</returns>
    public static T GetValueOrDefault<T>(this Result<T> result, T defaultValue = default!)
        => result.IsSuccess ? result.Value : defaultValue;

    /// <summary>
    /// Returns the success value, or throws when the result is a failure.
    /// When <paramref name="exceptionFactory"/> is provided, throws the custom exception it returns;
    /// otherwise throws <see cref="ResultException"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="exceptionFactory">
    /// Optional factory that maps the error to a custom exception.
    /// When <see langword="null"/>, a <see cref="ResultException"/> is thrown instead.
    /// </param>
    /// <returns>The success value.</returns>
    /// <exception cref="ResultException">Thrown when the result is a failure and no factory is provided.</exception>
    public static T GetValueOrThrow<T>(
        this Result<T> result,
        Func<IError, Exception>? exceptionFactory = null)
    {
        if (result.IsSuccess)
            return result.Value;

        if (exceptionFactory is not null)
            throw exceptionFactory(result.Error);

        // Delegate to Value getter, which calls ThrowHelper.ThrowResultFailure
        return result.Value;
    }

    /// <summary>
    /// Returns <see langword="true"/> and sets <paramref name="value"/> to the success value,
    /// or returns <see langword="false"/> and sets <paramref name="value"/> to <see langword="default"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="value">The success value, or <see langword="default"/> if the result is a failure.</param>
    /// <returns><see langword="true"/> if the result is a success; otherwise <see langword="false"/>.</returns>
    public static bool TryGetValue<T>(
        this Result<T> result,
        [MaybeNullWhen(false)] out T value)
    {
        if (result.IsSuccess)
        {
            value = result.Value;
            return true;
        }

        value = default;
        return false;
    }

    #endregion

    #region Finally

    /// <summary>
    /// Executes <paramref name="action"/> regardless of whether the result is a success or failure.
    /// Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The action to always execute, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static Result<T> Finally<T>(this Result<T> result, Action<Result<T>> action)
    {
        ResultGuard.NotNull(action);
        action(result);
        return result;
    }

    /// <summary>
    /// Executes <paramref name="action"/> regardless of whether the non-generic result is a success or failure.
    /// Returns the original result unchanged.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The action to always execute, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static Result Finally(this Result result, Action<Result> action)
    {
        ResultGuard.NotNull(action);
        action(result);
        return result;
    }

    #endregion

    #region Conversion

    /// <summary>
    /// Converts a <see cref="Result{T}"/> to a non-generic <see cref="Result"/>, discarding the value.
    /// A successful result becomes <see cref="Result.Success()"/>;
    /// a failed result becomes <see cref="Result.Failure(IError)"/> with the same error.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>A non-generic <see cref="Result"/> with the same outcome.</returns>
    public static Result ToResult<T>(this Result<T> result)
        => result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error);

    #endregion
}
