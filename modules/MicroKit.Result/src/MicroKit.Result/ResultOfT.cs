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
public readonly struct Result<T> : IEquatable<Result<T>>, IResultMarker
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
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag == TagSuccess;
    }

    /// <summary>Gets a value indicating whether this result represents a failure.</summary>
    /// <remarks>Returns <see langword="false"/> for an uninitialized (<c>default</c>) result.
    /// Accessing <see cref="Error"/> or <see cref="Value"/> on an uninitialized result throws <see cref="ResultException"/>.</remarks>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _tag == TagFailure;
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
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>: absence is a failure (for instance
    /// <see cref="Error.NotFound(ErrorCode, string)"/>), not a null value (ADR-RESULT-001 D6).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is itself a <see cref="Result"/> or a <see cref="Result{T}"/>
    /// (possible when <typeparamref name="T"/> is <see cref="object"/> or <see cref="ValueType"/>): a failure would otherwise become a
    /// success. Chain a result with <c>Bind</c> instead.
    /// </exception>
    public static Result<T> Success(T value)
    {
        if (value is null)
            ThrowHelper.ThrowSuccessValueNull(nameof(value));
        if (value is IResultMarker)
            ThrowHelper.ThrowResultAsValue(nameof(value));
        return new(value);
    }

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
    /// Executes the function and turns an exception it throws into a failure.
    /// </summary>
    /// <remarks>
    /// The failure carries an <see cref="ExceptionError"/>: a generic, client-safe message, with the
    /// exception kept in process for logging. An <see cref="OperationCanceledException"/> is not caught:
    /// cancellation propagates. Without a token to compare, an <c>HttpClient</c> timeout
    /// (<see cref="TaskCanceledException"/> with an inner <see cref="TimeoutException"/>) propagates too; a mapper
    /// overload turns it into an error.
    /// </remarks>
    /// <param name="func">The function to execute.</param>
    /// <returns>A successful result with the return value, or a failure wrapping the caught exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/> (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value.</exception>
    public static Result<T> Try(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        T value;
        try
        {
            value = func();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(new ExceptionError(ex));
        }

        return Success(value); // outside the catch: a null value is the delegate's contract violation
    }

    /// <summary>
    /// Executes the function and catches every exception, mapping it with the specified function.
    /// </summary>
    /// <remarks>The mapper receives every exception, <see cref="OperationCanceledException"/> included, and decides.</remarks>
    /// <param name="func">The function to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors; it must not return <see langword="null"/>.</param>
    /// <returns>A successful result with the return value, or a failure wrapping the mapped error.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/> (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exceptionMapper"/> returns <see langword="null"/>.</exception>
    public static Result<T> Try(Func<T> func, Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        T value;
        try
        {
            value = func();
        }
        catch (Exception ex)
        {
            return Failure(ThrowHelper.EnsureMapped(exceptionMapper(ex), ex));
        }

        return Success(value); // outside the catch: a null value is the delegate's contract violation
    }

    /// <summary>
    /// Executes the async function and turns an exception it throws into a failure.
    /// </summary>
    /// <remarks>
    /// The failure carries an <see cref="ExceptionError"/>: a generic, client-safe message, with the
    /// exception kept in process for logging. An <see cref="OperationCanceledException"/> is not caught:
    /// cancellation propagates. Without a token to compare, an <c>HttpClient</c> timeout
    /// (<see cref="TaskCanceledException"/> with an inner <see cref="TimeoutException"/>) propagates too; a mapper
    /// overload turns it into an error. Arguments are validated before the function starts.
    /// </remarks>
    /// <param name="func">The async function to execute.</param>
    /// <returns>A successful result with the return value, or a failure wrapping the caught exception.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/> (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when the delegate returns a result as the value.</exception>
    public static ValueTask<Result<T>> TryAsync(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return TryCoreAsync(func);

        static async ValueTask<Result<T>> TryCoreAsync(Func<Task<T>> func)
        {
            T value;
            try
            {
                value = await func().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new(new ExceptionError(ex));
            }

            return Success(value); // outside the catch: a null value is the delegate's contract violation
        }
    }

    /// <summary>
    /// Executes the async function and catches every exception, mapping it with the specified function.
    /// </summary>
    /// <remarks>
    /// The mapper receives every exception, <see cref="OperationCanceledException"/> included, and decides.
    /// Arguments are validated before the function starts.
    /// </remarks>
    /// <param name="func">The async function to execute.</param>
    /// <param name="exceptionMapper">Maps caught exceptions to errors; it must not return <see langword="null"/>.</param>
    /// <returns>A successful result with the return value, or a failure wrapping the mapped error.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the delegate returns <see langword="null"/> (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exceptionMapper"/> returns <see langword="null"/>.</exception>
    public static ValueTask<Result<T>> TryAsync(
        Func<Task<T>> func,
        Func<Exception, IError> exceptionMapper)
    {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(exceptionMapper);
        return TryCoreAsync(func, exceptionMapper);

        static async ValueTask<Result<T>> TryCoreAsync(Func<Task<T>> func, Func<Exception, IError> exceptionMapper)
        {
            T value;
            try
            {
                value = await func().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Failure(ThrowHelper.EnsureMapped(exceptionMapper(ex), ex));
            }

            return Success(value); // outside the catch: a null value is the delegate's contract violation
        }
    }

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

    /// <inheritdoc/>
    public override string ToString() => _tag switch
    {
        TagSuccess => $"Result {{ Success: {_value} }}",
        TagFailure => $"Result {{ Failure: [{_error!.Code}] {_error.Message} }}",
        _ => "Result { Uninitialized }",
    };

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    /// <summary>
    /// Implicitly converts a value to a successful <see cref="Result{T}"/>.
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/> (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is itself a <see cref="Result"/> or a <see cref="Result{T}"/>
    /// (possible when <typeparamref name="T"/> is <see cref="object"/> or <see cref="ValueType"/>): a failure would otherwise become a success.
    /// </exception>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Implicitly converts an error to a failed <see cref="Result{T}"/>.</summary>
    /// <remarks>
    /// Applies to an expression typed <see cref="Error"/> or a subtype; an expression typed <see cref="IError"/> does
    /// not convert (C# has no user-defined conversion from an interface): use <see cref="Failure(IError)"/>.
    /// For <typeparamref name="T"/> = <see cref="object"/>, <see cref="IError"/> or an unrelated type, an error converts
    /// to a failure. For <typeparamref name="T"/> = <see cref="Error"/> the conversion is ambiguous (CS0457), and for a
    /// subtype of <see cref="Error"/> the conversion from <typeparamref name="T"/> wins (a success): write
    /// <see cref="Failure(IError)"/> or <see cref="Success(T)"/> explicitly there (ADR-RESULT-001 D4). The
    /// <see langword="null"/> literal no longer converts to a result (CS0037).
    /// </remarks>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>Implicitly converts an error collection to a failed <see cref="Result{T}"/>.</summary>
    /// <remarks>
    /// For <typeparamref name="T"/> = <see cref="IReadOnlyList{T}"/> of <see cref="IError"/> or <see cref="object"/>, the
    /// collection converts to a failure; for <typeparamref name="T"/> = <see cref="ErrorCollection"/> the conversion is
    /// ambiguous (CS0457).
    /// </remarks>
    /// <param name="errors">The errors describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is <see langword="null"/>.</exception>
    public static implicit operator Result<T>(ErrorCollection errors) => Failure(errors);
}
