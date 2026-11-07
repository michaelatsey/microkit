namespace MicroKit.Result;

/// <summary>
/// Represents the outcome of an operation without a return value.
/// Use <see cref="Success()"/> for successful outcomes and <see cref="Failure(IError)"/> for failures.
/// </summary>
/// <example>
/// <code>
/// Result result = Result.Success();
/// Result failed = Result.Failure(new MyError());
/// </code>
/// </example>
public readonly struct Result : IEquatable<Result>, IResultMarker
{
    private const byte TagUninitialized = 0;
    private const byte TagSuccess = 1;
    private const byte TagFailure = 2;

    private readonly byte _tag;
    private readonly IError? _error;

    private Result(byte tag, IError? error)
    {
        _tag = tag;
        _error = error;
    }

    /// <summary>Gets a value indicating whether this result represents a success.</summary>
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag == TagSuccess;
    }

    /// <summary>Gets a value indicating whether this result represents a failure.</summary>
    /// <remarks>Returns <see langword="false"/> for an uninitialized (<c>default</c>) result.
    /// Accessing <see cref="Error"/> on an uninitialized result throws <see cref="ResultException"/>.</remarks>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag == TagFailure;
    }

    /// <summary>
    /// Gets the error if this result is a failure.
    /// </summary>
    /// <exception cref="ResultException">Thrown when the result is a success or uninitialized.</exception>
    public IError Error
    {
        get
        {
            if (_tag == TagSuccess)
                ThrowHelper.ThrowResultSuccess();
            if (_tag == TagUninitialized)
                ThrowHelper.ThrowResultUninitialized();
            return _error!;
        }
    }

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful <see cref="Result"/>.</returns>
    public static Result Success() => new(TagSuccess, null);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result"/>.</returns>
    public static Result Failure(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(TagFailure, error);
    }

    /// <summary>Creates a successful result with a value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    /// <summary>Creates a failed result with the specified error.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{T}"/>.</returns>
    public static Result<T> Failure<T>(IError error) => Result<T>.Failure(error);

    /// <summary>
    /// Executes the action and turns an exception it throws into a failure.
    /// </summary>
    /// <remarks>
    /// The failure carries an <see cref="ExceptionError"/>: a generic, client-safe message, with the
    /// exception kept in process for logging. An <see cref="OperationCanceledException"/> is not caught:
    /// cancellation propagates. Without a token to compare, an <c>HttpClient</c> timeout
    /// (<see cref="TaskCanceledException"/> with an inner <see cref="TimeoutException"/>) propagates too; a mapper
    /// overload turns it into an error.
    /// </remarks>
    /// <param name="action">The action to execute.</param>
    /// <returns>The result from the action, or a failure wrapping the caught exception.</returns>
    public static Result Try(Func<Result> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(new ExceptionError(ex));
        }
    }

    /// <summary>
    /// Executes the action and catches every exception, mapping it with the specified function.
    /// </summary>
    /// <remarks>The mapper receives every exception, <see cref="OperationCanceledException"/> included, and decides.</remarks>
    /// <param name="action">The action to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors; it must not return <see langword="null"/>.</param>
    /// <returns>The result from the action, or a failure wrapping the mapped error.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exceptionMapper"/> returns <see langword="null"/>.</exception>
    public static Result Try(Func<Result> action, Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return Failure(ThrowHelper.EnsureMapped(exceptionMapper(ex), ex));
        }
    }

    /// <summary>
    /// Executes the async action and turns an exception it throws into a failure.
    /// </summary>
    /// <remarks>
    /// The failure carries an <see cref="ExceptionError"/>: a generic, client-safe message, with the
    /// exception kept in process for logging. An <see cref="OperationCanceledException"/> is not caught:
    /// cancellation propagates. Without a token to compare, an <c>HttpClient</c> timeout
    /// (<see cref="TaskCanceledException"/> with an inner <see cref="TimeoutException"/>) propagates too; a mapper
    /// overload turns it into an error. Arguments are validated before the action starts.
    /// </remarks>
    /// <param name="action">The async action to execute.</param>
    /// <returns>The result from the action, or a failure wrapping the caught exception.</returns>
    public static ValueTask<Result> TryAsync(Func<Task<Result>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return TryCoreAsync(action);

        static async ValueTask<Result> TryCoreAsync(Func<Task<Result>> action)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failure(new ExceptionError(ex));
            }
        }
    }

    /// <summary>
    /// Executes the async action and catches every exception, mapping it with the specified function.
    /// </summary>
    /// <remarks>
    /// The mapper receives every exception, <see cref="OperationCanceledException"/> included, and decides.
    /// Arguments are validated before the action starts.
    /// </remarks>
    /// <param name="action">The async action to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors; it must not return <see langword="null"/>.</param>
    /// <returns>The result from the action, or a failure wrapping the mapped error.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exceptionMapper"/> returns <see langword="null"/>.</exception>
    public static ValueTask<Result> TryAsync(
        Func<Task<Result>> action,
        Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        return TryCoreAsync(action, exceptionMapper);

        static async ValueTask<Result> TryCoreAsync(Func<Task<Result>> action, Func<Exception, IError> exceptionMapper)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Failure(ThrowHelper.EnsureMapped(exceptionMapper(ex), ex));
            }
        }
    }

    /// <inheritdoc/>
    public bool Equals(Result other) =>
        _tag == other._tag && Equals(_error, other._error);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Result other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(_tag, _error);

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => _tag switch
    {
        TagSuccess => "Result { Success }",
        TagFailure => $"Result {{ Failure: [{_error!.Code}] {_error.Message} }}",
        _ => "Result { Uninitialized }",
    };
}
