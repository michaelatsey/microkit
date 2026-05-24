namespace MicroKit.Result;

/// <summary>
/// Represents the outcome of an operation that produces a value of type <typeparamref name="T"/>.
/// Use <see cref="Success(T)"/> for successful outcomes and <see cref="Failure(IError)"/> for failures.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <example>
/// <code>
/// Result&lt;int&gt; success = Result.Success(42);
/// Result&lt;int&gt; failure = Result.Failure&lt;int&gt;(new MyError());
/// int value = success.Match(v => v * 2, e => -1); // 84
/// </code>
/// </example>
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private const byte TagUninitialized = 0;
    private const byte TagSuccess = 1;
    private const byte TagFailure = 2;

    private readonly byte _tag;
    private readonly T? _value;
    private readonly IError? _error;

    private Result(T value)
    {
        _tag = TagSuccess;
        _value = value;
        _error = null;
    }

    private Result(IError error)
    {
        _tag = TagFailure;
        _value = default;
        _error = error;
    }

    /// <summary>Gets a value indicating whether this result represents a success.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag == TagSuccess;
    }

    /// <summary>Gets a value indicating whether this result represents a failure.</summary>
    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag != TagSuccess;
    }

    /// <summary>
    /// Gets the success value.
    /// </summary>
    /// <exception cref="ResultException">Thrown when the result is a failure or uninitialized.</exception>
    public T Value
    {
        get
        {
            if (_tag == TagFailure)
                ThrowHelper.ThrowResultFailure(_error!);
            if (_tag == TagUninitialized)
                ThrowHelper.ThrowResultUninitialized();
            return _value!;
        }
    }

    /// <summary>
    /// Gets the error.
    /// </summary>
    /// <exception cref="ResultException">Thrown when the result is a success.</exception>
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

    /// <summary>Creates a successful result with the specified value.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{T}"/>.</returns>
    public static Result<T> Failure(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    /// <summary>
    /// Executes the function and catches exceptions as failures.
    /// </summary>
    /// <param name="func">The function to execute.</param>
    /// <returns>A successful result with the return value, or a failure wrapping any caught exception.</returns>
    public static Result<T> Try(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        try
        {
            return new(func());
        }
        catch (Exception ex)
        {
            return new(new ExceptionError(ex));
        }
    }

    /// <summary>
    /// Executes the function and catches exceptions, mapping them with the specified function.
    /// </summary>
    /// <param name="func">The function to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    /// <returns>A successful result with the return value, or a failure wrapping the mapped error.</returns>
    public static Result<T> Try(Func<T> func, Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        try
        {
            return new(func());
        }
        catch (Exception ex)
        {
            return new(exceptionMapper(ex));
        }
    }

    /// <summary>
    /// Executes the async function and catches exceptions as failures.
    /// </summary>
    /// <param name="func">The async function to execute.</param>
    /// <returns>A successful result with the return value, or a failure wrapping any caught exception.</returns>
    public static async ValueTask<Result<T>> TryAsync(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        try
        {
            return new(await func().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new(new ExceptionError(ex));
        }
    }

    /// <summary>
    /// Executes the async function and catches exceptions, mapping them with the specified function.
    /// </summary>
    /// <param name="func">The async function to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors.</param>
    /// <returns>A successful result with the return value, or a failure wrapping the mapped error.</returns>
    public static async ValueTask<Result<T>> TryAsync(
        Func<Task<T>> func,
        Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        try
        {
            return new(await func().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new(exceptionMapper(ex));
        }
    }

    /// <summary>
    /// Implicitly converts a value to a successful <see cref="Result{T}"/>.
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <inheritdoc/>
    public bool Equals(Result<T> other) =>
        _tag == other._tag
        && EqualityComparer<T>.Default.Equals(_value, other._value)
        && Equals(_error, other._error);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Result<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(_tag, _value, _error);

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => _tag switch
    {
        TagSuccess => $"Result {{ Success: {_value} }}",
        TagFailure => $"Result {{ Failure: [{_error!.Code}] {_error.Message} }}",
        _ => "Result { Uninitialized }",
    };
}
