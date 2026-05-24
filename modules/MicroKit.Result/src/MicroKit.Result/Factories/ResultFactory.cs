namespace MicroKit.Result;

/// <summary>
/// Provides static factory methods for creating <see cref="Result"/> and <see cref="Result{T}"/> instances.
/// Thin convenience wrapper delegating to the factory methods on <see cref="Result"/>.
/// </summary>
public static class ResultFactory
{
    /// <summary>Creates a successful non-generic result.</summary>
    public static Result Success() => Result.Success();

    /// <summary>Creates a successful result with the specified value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The success value.</param>
    public static Result<T> Success<T>(T value) => Result.Success(value);

    /// <summary>Creates a failed non-generic result.</summary>
    /// <param name="error">The error describing the failure.</param>
    public static Result Failure(IError error) => Result.Failure(error);

    /// <summary>Creates a failed generic result.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="error">The error describing the failure.</param>
    public static Result<T> Failure<T>(IError error) => Result.Failure<T>(error);

    /// <summary>Executes the action, catching exceptions as failures.</summary>
    /// <param name="action">The action to execute.</param>
    public static Result Try(Func<Result> action) => Result.Try(action);

    /// <summary>Executes the action, catching exceptions with a custom mapper.</summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    public static Result Try(Func<Result> action, Func<Exception, IError> exceptionMapper) =>
        Result.Try(action, exceptionMapper);

    /// <summary>Executes the function, catching exceptions as failures.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="func">The function to execute.</param>
    public static Result<T> Try<T>(Func<T> func) => Result<T>.Try(func);

    /// <summary>Executes the function, catching exceptions with a custom mapper.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    public static Result<T> Try<T>(Func<T> func, Func<Exception, IError> exceptionMapper) =>
        Result<T>.Try(func, exceptionMapper);

    /// <summary>Executes the async action, catching exceptions as failures.</summary>
    /// <param name="action">The async action to execute.</param>
    public static ValueTask<Result> TryAsync(Func<Task<Result>> action) =>
        Result.TryAsync(action);

    /// <summary>Executes the async action, catching exceptions with a custom mapper.</summary>
    /// <param name="action">The async action to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    public static ValueTask<Result> TryAsync(
        Func<Task<Result>> action, Func<Exception, IError> exceptionMapper) =>
        Result.TryAsync(action, exceptionMapper);

    /// <summary>Executes the async function, catching exceptions as failures.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="func">The async function to execute.</param>
    public static ValueTask<Result<T>> TryAsync<T>(Func<Task<T>> func) =>
        Result<T>.TryAsync(func);

    /// <summary>Executes the async function, catching exceptions with a custom mapper.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="func">The async function to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    public static ValueTask<Result<T>> TryAsync<T>(
        Func<Task<T>> func, Func<Exception, IError> exceptionMapper) =>
        Result<T>.TryAsync(func, exceptionMapper);
}
