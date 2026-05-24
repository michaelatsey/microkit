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
public readonly struct Result : IEquatable<Result>
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
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag == TagSuccess;
    }

    /// <summary>Gets a value indicating whether this result represents a failure.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag != TagSuccess;
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
    /// Executes the action and catches exceptions as failures.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>The result from the action, or a failure wrapping any caught exception.</returns>
    public static Result Try(Func<Result> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return Failure(new ExceptionError(ex));
        }
    }

    /// <summary>
    /// Executes the action and catches exceptions, mapping them with the specified function.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    /// <returns>The result from the action, or a failure wrapping the mapped error.</returns>
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
            return Failure(exceptionMapper(ex));
        }
    }

    /// <summary>
    /// Executes the async action and catches exceptions as failures.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <returns>The result from the action, or a failure wrapping any caught exception.</returns>
    public static async ValueTask<Result> TryAsync(Func<Task<Result>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failure(new ExceptionError(ex));
        }
    }

    /// <summary>
    /// Executes the async action and catches exceptions, mapping them with the specified function.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    /// <returns>The result from the action, or a failure wrapping the mapped error.</returns>
    public static async ValueTask<Result> TryAsync(
        Func<Task<Result>> action,
        Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failure(exceptionMapper(ex));
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
