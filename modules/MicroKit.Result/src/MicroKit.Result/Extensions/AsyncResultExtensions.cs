namespace MicroKit.Result;

/// <summary>
/// Async extension methods for <see cref="Result{T}"/>, <see cref="Task{TResult}"/>
/// and <see cref="ValueTask{TResult}"/> wrapping results.
/// All methods use <see cref="Task.ConfigureAwait(bool)"/> with <c>false</c>.
/// </summary>
public static class AsyncResultExtensions
{
    #region Map

    /// <summary>Asynchronously maps the value of a successful result.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="mapper">The async transformation function.</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> MapAsync<TIn, TOut>(
        this Result<TIn> result, Func<TIn, Task<TOut>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return result.IsSuccess
            ? Result<TOut>.Success(await mapper(result.Value).ConfigureAwait(false))
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Awaits the task and maps the value.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="mapper">The transformation function.</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> Map<TIn, TOut>(
        this Task<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(mapper);
    }

    /// <summary>Awaits the value task and maps the value.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="mapper">The transformation function.</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> Map<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(mapper);
    }

    #endregion

    #region Bind

    /// <summary>Asynchronously chains a result-producing function.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The async function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> BindAsync<TIn, TOut>(
        this Result<TIn> result, Func<TIn, Task<Result<TOut>>> binder)
    {
        ResultGuard.NotNull(binder);
        return result.IsSuccess
            ? await binder(result.Value).ConfigureAwait(false)
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Awaits the task and chains a result-producing function.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="binder">The function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(binder);
    }

    /// <summary>Awaits the value task and chains a result-producing function.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="binder">The function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static async ValueTask<Result<TOut>> Bind<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(binder);
    }

    #endregion

    #region Match

    /// <summary>Asynchronously matches success or failure.</summary>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="result">The result to match.</param>
    /// <param name="onSuccess">Executed asynchronously on success.</param>
    /// <param name="onFailure">Executed asynchronously on failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static async ValueTask<TOut> MatchAsync<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<TOut>> onSuccess,
        Func<IError, Task<TOut>> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return result.IsSuccess
            ? await onSuccess(result.Value).ConfigureAwait(false)
            : await onFailure(result.Error).ConfigureAwait(false);
    }

    /// <summary>Awaits the task and matches success or failure.</summary>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static async ValueTask<TOut> Match<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<IError, TOut> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    /// <summary>Awaits the value task and matches success or failure.</summary>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static async ValueTask<TOut> Match<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<IError, TOut> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    #endregion

    #region Tap

    /// <summary>Asynchronously executes a side-effect on success.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> TapAsync<T>(
        this Result<T> result, Func<T, Task> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsSuccess)
            await action(result.Value).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the task and executes a side-effect on success.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> Tap<T>(
        this Task<Result<T>> resultTask, Action<T> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.Tap(action);
    }

    /// <summary>Awaits the value task and executes a side-effect on success.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> Tap<T>(
        this ValueTask<Result<T>> resultTask, Action<T> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.Tap(action);
    }

    #endregion

    #region TapError

    /// <summary>Asynchronously executes a side-effect on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> TapErrorAsync<T>(
        this Result<T> result, Func<IError, Task> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsFailure)
            await action(result.Error).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the task and executes a side-effect on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> TapError<T>(
        this Task<Result<T>> resultTask, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.TapError(action);
    }

    /// <summary>Awaits the value task and executes a side-effect on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> TapError<T>(
        this ValueTask<Result<T>> resultTask, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.TapError(action);
    }

    #endregion

    #region Ensure

    /// <summary>Asynchronously validates the value against a predicate.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The async validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static async ValueTask<Result<T>> EnsureAsync<T>(
        this Result<T> result, Func<T, Task<bool>> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);

        if (result.IsFailure)
            return result;

        return await predicate(result.Value).ConfigureAwait(false)
            ? result
            : Result<T>.Failure(error);
    }

    /// <summary>Awaits the task and validates the value against a predicate.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="predicate">The validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static async ValueTask<Result<T>> Ensure<T>(
        this Task<Result<T>> resultTask, Func<T, bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        var result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>Awaits the value task and validates the value against a predicate.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="predicate">The validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static async ValueTask<Result<T>> Ensure<T>(
        this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        var result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    #endregion

    #region MapError

    /// <summary>Asynchronously transforms the error of a failed result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The async error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static async ValueTask<Result<T>> MapErrorAsync<T>(
        this Result<T> result, Func<IError, Task<IError>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return result.IsFailure
            ? Result<T>.Failure(await mapper(result.Error).ConfigureAwait(false))
            : result;
    }

    /// <summary>Awaits the task and transforms the error on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="mapper">The error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static async ValueTask<Result<T>> MapError<T>(
        this Task<Result<T>> resultTask, Func<IError, IError> mapper)
    {
        ResultGuard.NotNull(mapper);
        var result = await resultTask.ConfigureAwait(false);
        return result.MapError(mapper);
    }

    /// <summary>Awaits the value task and transforms the error on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="mapper">The error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static async ValueTask<Result<T>> MapError<T>(
        this ValueTask<Result<T>> resultTask, Func<IError, IError> mapper)
    {
        ResultGuard.NotNull(mapper);
        var result = await resultTask.ConfigureAwait(false);
        return result.MapError(mapper);
    }

    #endregion

    #region Compensate

    /// <summary>Asynchronously provides a fallback result when the result is a failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The async fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static async ValueTask<Result<T>> CompensateAsync<T>(
        this Result<T> result, Func<IError, Task<Result<T>>> fallback)
    {
        ResultGuard.NotNull(fallback);
        return result.IsFailure
            ? await fallback(result.Error).ConfigureAwait(false)
            : result;
    }

    /// <summary>Awaits the task and provides a fallback result on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="fallback">The fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static async ValueTask<Result<T>> Compensate<T>(
        this Task<Result<T>> resultTask, Func<IError, Result<T>> fallback)
    {
        ResultGuard.NotNull(fallback);
        var result = await resultTask.ConfigureAwait(false);
        return result.Compensate(fallback);
    }

    /// <summary>Awaits the value task and provides a fallback result on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="fallback">The fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static async ValueTask<Result<T>> Compensate<T>(
        this ValueTask<Result<T>> resultTask, Func<IError, Result<T>> fallback)
    {
        ResultGuard.NotNull(fallback);
        var result = await resultTask.ConfigureAwait(false);
        return result.Compensate(fallback);
    }

    #endregion

    #region Finally

    /// <summary>
    /// Asynchronously executes <paramref name="action"/> regardless of success or failure.
    /// Returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action to always execute, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> FinallyAsync<T>(
        this Result<T> result, Func<Result<T>, Task> action)
    {
        ResultGuard.NotNull(action);
        await action(result).ConfigureAwait(false);
        return result;
    }

    /// <summary>Awaits the task and always executes the action regardless of outcome.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="action">The action to always execute.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> Finally<T>(
        this Task<Result<T>> resultTask, Action<Result<T>> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.Finally(action);
    }

    /// <summary>Awaits the value task and always executes the action regardless of outcome.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="action">The action to always execute.</param>
    /// <returns>The original result.</returns>
    public static async ValueTask<Result<T>> Finally<T>(
        this ValueTask<Result<T>> resultTask, Action<Result<T>> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.Finally(action);
    }

    #endregion
}
