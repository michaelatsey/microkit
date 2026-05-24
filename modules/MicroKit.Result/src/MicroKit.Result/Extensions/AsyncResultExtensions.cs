namespace MicroKit.Result;

/// <summary>
/// Async extension methods for <see cref="Result{T}"/>, <see cref="Task{TResult}"/>
/// and <see cref="ValueTask{TResult}"/> wrapping results.
/// All methods use <see cref="Task.ConfigureAwait(bool)"/> with <c>false</c>.
/// </summary>
public static class AsyncResultExtensions
{
    // ──────────────────────────────────────────────
    //  Surface 1: On Result<T> with async delegates
    // ──────────────────────────────────────────────

    /// <summary>Asynchronously maps the value of a successful result.</summary>
    public static async ValueTask<Result<TOut>> MapAsync<TIn, TOut>(
        this Result<TIn> result, Func<TIn, Task<TOut>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return result.IsSuccess
            ? Result<TOut>.Success(await mapper(result.Value).ConfigureAwait(false))
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Asynchronously chains a result-producing function.</summary>
    public static async ValueTask<Result<TOut>> BindAsync<TIn, TOut>(
        this Result<TIn> result, Func<TIn, Task<Result<TOut>>> binder)
    {
        ResultGuard.NotNull(binder);
        return result.IsSuccess
            ? await binder(result.Value).ConfigureAwait(false)
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Asynchronously matches success or failure.</summary>
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

    /// <summary>Asynchronously executes a side-effect on success.</summary>
    public static async ValueTask<Result<T>> TapAsync<T>(
        this Result<T> result, Func<T, Task> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsSuccess)
            await action(result.Value).ConfigureAwait(false);
        return result;
    }

    /// <summary>Asynchronously executes a side-effect on failure.</summary>
    public static async ValueTask<Result<T>> TapErrorAsync<T>(
        this Result<T> result, Func<IError, Task> action)
    {
        ResultGuard.NotNull(action);
        if (result.IsFailure)
            await action(result.Error).ConfigureAwait(false);
        return result;
    }

    /// <summary>Asynchronously validates the value against a predicate.</summary>
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

    // ──────────────────────────────────────────────
    //  Surface 2: On Task<Result<T>> (sync delegates)
    // ──────────────────────────────────────────────

    /// <summary>Awaits the task and maps the value.</summary>
    public static async ValueTask<Result<TOut>> Map<TIn, TOut>(
        this Task<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(mapper);
    }

    /// <summary>Awaits the task and chains a result-producing function.</summary>
    public static async ValueTask<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(binder);
    }

    /// <summary>Awaits the task and matches success or failure.</summary>
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

    /// <summary>Awaits the task and executes a side-effect on success.</summary>
    public static async ValueTask<Result<T>> Tap<T>(
        this Task<Result<T>> resultTask, Action<T> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.Tap(action);
    }

    /// <summary>Awaits the task and executes a side-effect on failure.</summary>
    public static async ValueTask<Result<T>> TapError<T>(
        this Task<Result<T>> resultTask, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.TapError(action);
    }

    /// <summary>Awaits the task and validates the value against a predicate.</summary>
    public static async ValueTask<Result<T>> Ensure<T>(
        this Task<Result<T>> resultTask, Func<T, bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        var result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    // ──────────────────────────────────────────────
    //  Surface 3: On ValueTask<Result<T>> (sync delegates)
    // ──────────────────────────────────────────────

    /// <summary>Awaits the value task and maps the value.</summary>
    public static async ValueTask<Result<TOut>> Map<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(mapper);
    }

    /// <summary>Awaits the value task and chains a result-producing function.</summary>
    public static async ValueTask<Result<TOut>> Bind<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(binder);
    }

    /// <summary>Awaits the value task and matches success or failure.</summary>
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

    /// <summary>Awaits the value task and executes a side-effect on success.</summary>
    public static async ValueTask<Result<T>> Tap<T>(
        this ValueTask<Result<T>> resultTask, Action<T> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.Tap(action);
    }

    /// <summary>Awaits the value task and executes a side-effect on failure.</summary>
    public static async ValueTask<Result<T>> TapError<T>(
        this ValueTask<Result<T>> resultTask, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        var result = await resultTask.ConfigureAwait(false);
        return result.TapError(action);
    }

    /// <summary>Awaits the value task and validates the value against a predicate.</summary>
    public static async ValueTask<Result<T>> Ensure<T>(
        this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        var result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }
}
