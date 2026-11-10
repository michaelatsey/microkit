namespace MicroKit.Result;

/// <summary>
/// Async extension methods for <see cref="Result{T}"/>, <see cref="Result"/>, <see cref="Task{TResult}"/>
/// and <see cref="ValueTask{TResult}"/> wrapping results.
/// </summary>
/// <remarks>
/// <para>
/// Every method validates its arguments (delegates, errors, the source task) when it is called, before anything
/// is awaited, so a synchronous and an asynchronous caller see the same exception at the same point. The state
/// of the result itself is read once it is available: a <c>default</c> result throws <see cref="ResultException"/>
/// at the await. Every await uses <c>ConfigureAwait(false)</c>.
/// </para>
/// <para>
/// The overloads whose delegate takes a <see cref="CancellationToken"/> take <c>CancellationToken ct = default</c>
/// last and pass it to the delegate. Omitted, the delegate receives <see cref="CancellationToken.None"/>; a lambda
/// parameter named <c>ct</c> then shadows the caller's token. Pass it explicitly (CA2016 reports a missing one).
/// </para>
/// <para>
/// <c>Tap</c>, <c>TapError</c> and <c>Finally</c> given a delegate that returns a <see cref="Task"/>, an async
/// lambda included, bind to the overloads here, which await the side effect. A non-async lambda that returns a
/// <see cref="ValueTask"/> binds to the synchronous <see cref="Action{T}"/> overload instead, and its
/// <see cref="ValueTask"/> is discarded (CA2012 reports it): write it <c>async x =&gt; await port(x)</c>.
/// </para>
/// </remarks>
public static partial class AsyncResultExtensions
{
    #region Map

    /// <summary>Asynchronously maps the value of a successful result.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="mapper">The async transformation function.</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static ValueTask<Result<TOut>> MapAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<TOut>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper);

        static async ValueTask<Result<TOut>> Core(Result<TIn> result, Func<TIn, Task<TOut>> mapper)
        {
            return result.IsSuccess
                ? Result<TOut>.Success(await mapper(result.Value).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the task and maps the value.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="mapper">The transformation function.</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static ValueTask<Result<TOut>> Map<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(mapper);
        return Core(resultTask, mapper);

        static async ValueTask<Result<TOut>> Core(Task<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
        {
            return (await resultTask.ConfigureAwait(false)).Map(mapper);
        }
    }

    /// <summary>Awaits the value task and maps the value.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="mapper">The transformation function.</param>
    /// <returns>A result with the mapped value, or the original failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/>: a success needs a value (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value; use Bind.</exception>
    public static ValueTask<Result<TOut>> Map<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
    {
        ResultGuard.NotNull(mapper);
        return Core(resultTask, mapper);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> mapper)
        {
            return (await resultTask.ConfigureAwait(false)).Map(mapper);
        }
    }
    #endregion

    #region Bind

    /// <summary>Asynchronously chains a result-producing function.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="binder">The async function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result<TOut>> BindAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<Result<TOut>>> binder)
    {
        ResultGuard.NotNull(binder);
        return Core(result, binder);

        static async ValueTask<Result<TOut>> Core(Result<TIn> result, Func<TIn, Task<Result<TOut>>> binder)
        {
            return result.IsSuccess
                ? await binder(result.Value).ConfigureAwait(false)
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the task and chains a result-producing function.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="binder">The function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result<TOut>> Bind<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(binder);
        return Core(resultTask, binder);

        static async ValueTask<Result<TOut>> Core(Task<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
        {
            return (await resultTask.ConfigureAwait(false)).Bind(binder);
        }
    }

    /// <summary>Awaits the value task and chains a result-producing function.</summary>
    /// <typeparam name="TIn">The source value type.</typeparam>
    /// <typeparam name="TOut">The target value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="binder">The function producing a new result.</param>
    /// <returns>The result from the binder, or the original failure.</returns>
    public static ValueTask<Result<TOut>> Bind<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
    {
        ResultGuard.NotNull(binder);
        return Core(resultTask, binder);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result<TIn>> resultTask, Func<TIn, Result<TOut>> binder)
        {
            return (await resultTask.ConfigureAwait(false)).Bind(binder);
        }
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
    public static ValueTask<TOut> MatchAsync<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<TOut>> onSuccess, Func<IError, Task<TOut>> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return Core(result, onSuccess, onFailure);

        static async ValueTask<TOut> Core(Result<TIn> result, Func<TIn, Task<TOut>> onSuccess, Func<IError, Task<TOut>> onFailure)
        {
            return result.IsSuccess
                ? await onSuccess(result.Value).ConfigureAwait(false)
                : await onFailure(result.Error).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits the task and matches success or failure.</summary>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static ValueTask<TOut> Match<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<IError, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async ValueTask<TOut> Core(Task<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<IError, TOut> onFailure)
        {
            return (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }
    }

    /// <summary>Awaits the value task and matches success or failure.</summary>
    /// <typeparam name="TIn">The value type.</typeparam>
    /// <typeparam name="TOut">The return type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="onSuccess">Executed on success.</param>
    /// <param name="onFailure">Executed on failure.</param>
    /// <returns>The value produced by the matching function.</returns>
    public static ValueTask<TOut> Match<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<IError, TOut> onFailure)
    {
        ResultGuard.NotNull(onSuccess);
        ResultGuard.NotNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async ValueTask<TOut> Core(ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> onSuccess, Func<IError, TOut> onFailure)
        {
            return (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }
    }
    #endregion

    #region Tap

    /// <summary>Asynchronously executes a side effect on the value of a successful result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapAsync<T>(this Result<T> result, Func<T, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(result, action);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<T, Task> action)
        {
            if (result.IsSuccess)
                await action(result.Value).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Executes a side effect on the value of a successful result and awaits it. An async lambda passed to <c>Tap</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Tap<T>(this Result<T> result, Func<T, Task> action) =>
        result.TapAsync(action);

    /// <summary>Executes a side effect when a non-generic result is successful, and awaits it. An async lambda passed to <c>Tap</c> binds here.</summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> Tap(this Result result, Func<Task> action) =>
        result.TapAsync(action);

    /// <summary>Awaits the task and executes a side effect on the value of a successful result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Tap<T>(this Task<Result<T>> resultTask, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Action<T> action)
        {
            return (await resultTask.ConfigureAwait(false)).Tap(action);
        }
    }

    /// <summary>Awaits the task, then executes a side effect on the value of a successful result and awaits it. An async lambda passed to <c>Tap</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Tap<T>(this Task<Result<T>> resultTask, Func<T, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Func<T, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsSuccess)
                await action(result.Value).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Awaits the value task and executes a side effect on the value of a successful result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Tap<T>(this ValueTask<Result<T>> resultTask, Action<T> action)
    {
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Action<T> action)
        {
            return (await resultTask.ConfigureAwait(false)).Tap(action);
        }
    }

    /// <summary>Awaits the value task, then executes a side effect on the value of a successful result and awaits it. An async lambda passed to <c>Tap</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Tap<T>(this ValueTask<Result<T>> resultTask, Func<T, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<T, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsSuccess)
                await action(result.Value).ConfigureAwait(false);
            return result;
        }
    }
    #endregion

    #region TapError

    /// <summary>Asynchronously executes a side effect on the error of a failed result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapErrorAsync<T>(this Result<T> result, Func<IError, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(result, action);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<IError, Task> action)
        {
            if (result.IsFailure)
                await action(result.Error).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Executes a side effect on the error of a failed result and awaits it. An async lambda passed to <c>TapError</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapError<T>(this Result<T> result, Func<IError, Task> action) =>
        result.TapErrorAsync(action);

    /// <summary>Executes a side effect on the error of a failed non-generic result, and awaits it. An async lambda passed to <c>TapError</c> binds here.</summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> TapError(this Result result, Func<IError, Task> action) =>
        result.TapErrorAsync(action);

    /// <summary>Awaits the task and executes a side effect on the error of a failed result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapError<T>(this Task<Result<T>> resultTask, Action<IError> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Action<IError> action)
        {
            return (await resultTask.ConfigureAwait(false)).TapError(action);
        }
    }

    /// <summary>Awaits the task, then executes a side effect on the error of a failed result and awaits it. An async lambda passed to <c>TapError</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapError<T>(this Task<Result<T>> resultTask, Func<IError, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Func<IError, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                await action(result.Error).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Awaits the value task and executes a side effect on the error of a failed result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="action">The side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapError<T>(this ValueTask<Result<T>> resultTask, Action<IError> action)
    {
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Action<IError> action)
        {
            return (await resultTask.ConfigureAwait(false)).TapError(action);
        }
    }

    /// <summary>Awaits the value task, then executes a side effect on the error of a failed result and awaits it. An async lambda passed to <c>TapError</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="action">The async side-effect action.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> TapError<T>(this ValueTask<Result<T>> resultTask, Func<IError, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<IError, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                await action(result.Error).ConfigureAwait(false);
            return result;
        }
    }
    #endregion

    #region Ensure

    /// <summary>Asynchronously validates the value against a predicate.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="predicate">The async validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static ValueTask<Result<T>> EnsureAsync<T>(this Result<T> result, Func<T, Task<bool>> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(result, predicate, error);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<T, Task<bool>> predicate, IError error)
        {
            if (result.IsFailure)
                return result;

            return await predicate(result.Value).ConfigureAwait(false)
                ? result
                : Result<T>.Failure(error);
        }
    }

    /// <summary>Awaits the task and validates the value against a predicate.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the result.</param>
    /// <param name="predicate">The validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static ValueTask<Result<T>> Ensure<T>(this Task<Result<T>> resultTask, Func<T, bool> predicate, IError error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Func<T, bool> predicate, IError error)
        {
            return (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);
        }
    }

    /// <summary>Awaits the value task and validates the value against a predicate.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the result.</param>
    /// <param name="predicate">The validation predicate.</param>
    /// <param name="error">The error to use when the predicate fails.</param>
    /// <returns>The original result if valid, or a failure.</returns>
    public static ValueTask<Result<T>> Ensure<T>(this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, IError error)
    {
        ResultGuard.NotNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<T, bool> predicate, IError error)
        {
            return (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);
        }
    }
    #endregion

    #region MapError

    /// <summary>Asynchronously transforms the error of a failed result.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="mapper">The async error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static ValueTask<Result<T>> MapErrorAsync<T>(this Result<T> result, Func<IError, Task<IError>> mapper)
    {
        ResultGuard.NotNull(mapper);
        return Core(result, mapper);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<IError, Task<IError>> mapper)
        {
            return result.IsSuccess
                ? result
                : Result<T>.Failure(await mapper(result.Error).ConfigureAwait(false)); // Error throws on a default result
        }
    }

    /// <summary>Awaits the task and transforms the error on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="mapper">The error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static ValueTask<Result<T>> MapError<T>(this Task<Result<T>> resultTask, Func<IError, IError> mapper)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(mapper);
        return Core(resultTask, mapper);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Func<IError, IError> mapper)
        {
            return (await resultTask.ConfigureAwait(false)).MapError(mapper);
        }
    }

    /// <summary>Awaits the value task and transforms the error on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="mapper">The error transformation function.</param>
    /// <returns>The original result if successful, or a new failure with the mapped error.</returns>
    public static ValueTask<Result<T>> MapError<T>(this ValueTask<Result<T>> resultTask, Func<IError, IError> mapper)
    {
        ResultGuard.NotNull(mapper);
        return Core(resultTask, mapper);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<IError, IError> mapper)
        {
            return (await resultTask.ConfigureAwait(false)).MapError(mapper);
        }
    }
    #endregion

    #region Compensate

    /// <summary>Asynchronously provides a fallback result when the result is a failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="fallback">The async fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static ValueTask<Result<T>> CompensateAsync<T>(this Result<T> result, Func<IError, Task<Result<T>>> fallback)
    {
        ResultGuard.NotNull(fallback);
        return Core(result, fallback);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<IError, Task<Result<T>>> fallback)
        {
            return result.IsSuccess
                ? result
                : await fallback(result.Error).ConfigureAwait(false); // Error throws on a default result
        }
    }

    /// <summary>Awaits the task and provides a fallback result on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="fallback">The fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static ValueTask<Result<T>> Compensate<T>(this Task<Result<T>> resultTask, Func<IError, Result<T>> fallback)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(fallback);
        return Core(resultTask, fallback);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Func<IError, Result<T>> fallback)
        {
            return (await resultTask.ConfigureAwait(false)).Compensate(fallback);
        }
    }

    /// <summary>Awaits the value task and provides a fallback result on failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="fallback">The fallback function that receives the original error.</param>
    /// <returns>The original result if successful, or the fallback result.</returns>
    public static ValueTask<Result<T>> Compensate<T>(this ValueTask<Result<T>> resultTask, Func<IError, Result<T>> fallback)
    {
        ResultGuard.NotNull(fallback);
        return Core(resultTask, fallback);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<IError, Result<T>> fallback)
        {
            return (await resultTask.ConfigureAwait(false)).Compensate(fallback);
        }
    }
    #endregion

    #region Finally

    /// <summary>Asynchronously executes <paramref name="action"/> regardless of success or failure. Returns the original result unchanged.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action to always execute, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> FinallyAsync<T>(this Result<T> result, Func<Result<T>, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(result, action);

        static async ValueTask<Result<T>> Core(Result<T> result, Func<Result<T>, Task> action)
        {
            await action(result).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Executes <paramref name="action"/> regardless of success or failure and awaits it. An async lambda passed to <c>Finally</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action to always execute, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Finally<T>(this Result<T> result, Func<Result<T>, Task> action) =>
        result.FinallyAsync(action);

    /// <summary>Executes <paramref name="action"/> on a non-generic result, whatever its outcome, and awaits it. An async lambda passed to <c>Finally</c> binds here.</summary>
    /// <param name="result">The result.</param>
    /// <param name="action">The async action to always execute, receiving the full result.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result> Finally(this Result result, Func<Result, Task> action) =>
        result.FinallyAsync(action);

    /// <summary>Awaits the task and always executes the action regardless of outcome.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="action">The action to always execute.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Finally<T>(this Task<Result<T>> resultTask, Action<Result<T>> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Action<Result<T>> action)
        {
            return (await resultTask.ConfigureAwait(false)).Finally(action);
        }
    }

    /// <summary>Awaits the task, then always executes the action and awaits it. An async lambda passed to <c>Finally</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The task wrapping the source result.</param>
    /// <param name="action">The async action to always execute.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Finally<T>(this Task<Result<T>> resultTask, Func<Result<T>, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(Task<Result<T>> resultTask, Func<Result<T>, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            await action(result).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>Awaits the value task and always executes the action regardless of outcome.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="action">The action to always execute.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Finally<T>(this ValueTask<Result<T>> resultTask, Action<Result<T>> action)
    {
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Action<Result<T>> action)
        {
            return (await resultTask.ConfigureAwait(false)).Finally(action);
        }
    }

    /// <summary>Awaits the value task, then always executes the action and awaits it. An async lambda passed to <c>Finally</c> binds here.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="resultTask">The value task wrapping the source result.</param>
    /// <param name="action">The async action to always execute.</param>
    /// <returns>The original result.</returns>
    public static ValueTask<Result<T>> Finally<T>(this ValueTask<Result<T>> resultTask, Func<Result<T>, Task> action)
    {
        ResultGuard.NotNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<Result<T>, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            await action(result).ConfigureAwait(false);
            return result;
        }
    }
    #endregion
}
