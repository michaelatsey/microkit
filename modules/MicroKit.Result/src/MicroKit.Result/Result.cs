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
    /// <param name="value">The success value; not <see langword="null"/>, and not itself a result.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/> (ADR-RESULT-001 D6).</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is itself a result.</exception>
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

    /// <summary>
    /// Combines multiple results. Fails fast on the first failure.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>A single successful result, or the first failure encountered.</returns>
    /// <exception cref="ResultException">Thrown when a result before the first failure is uninitialized (<c>default</c>).</exception>
    public static Result Combine(params ReadOnlySpan<Result> results)
    {
        foreach (var result in results)
        {
            if (!result.IsSuccess)
                return Failure(result.Error); // Error throws ResultException on a default result
        }
        return Success();
    }

    /// <summary>
    /// Combines multiple results, collecting ALL errors into an <see cref="ErrorCollection"/>.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>A single successful result, or a failure with all collected errors.</returns>
    /// <exception cref="ResultException">Thrown when a result is uninitialized (<c>default</c>).</exception>
    public static Result CombineAll(params ReadOnlySpan<Result> results)
    {
        List<IError>? errors = null;
        foreach (var result in results)
        {
            if (!result.IsSuccess)
            {
                errors ??= [];
                errors.Add(result.Error); // throws ResultException on a default result
            }
        }

        return errors is null
            ? Success()
            : Failure(ErrorCollection.From(errors));
    }

    /// <summary>
    /// Combines two generic results into a tuple. Fails fast on the first failure.
    /// </summary>
    /// <typeparam name="T1">The first value type.</typeparam>
    /// <typeparam name="T2">The second value type.</typeparam>
    /// <param name="first">The first result.</param>
    /// <param name="second">The second result.</param>
    /// <returns>A success holding both values, or the first failure.</returns>
    /// <exception cref="ResultException">Thrown when a result before the first failure is uninitialized (<c>default</c>).</exception>
    public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> first, Result<T2> second)
    {
        if (first.IsFailure) return Result<(T1, T2)>.Failure(first.Error);
        if (second.IsFailure) return Result<(T1, T2)>.Failure(second.Error);
        return Result<(T1, T2)>.Success((first.Value, second.Value));
    }

    /// <summary>
    /// Combines three generic results into a tuple. Fails fast on the first failure.
    /// </summary>
    /// <typeparam name="T1">The first value type.</typeparam>
    /// <typeparam name="T2">The second value type.</typeparam>
    /// <typeparam name="T3">The third value type.</typeparam>
    /// <param name="first">The first result.</param>
    /// <param name="second">The second result.</param>
    /// <param name="third">The third result.</param>
    /// <returns>A success holding the three values, or the first failure.</returns>
    /// <exception cref="ResultException">Thrown when a result before the first failure is uninitialized (<c>default</c>).</exception>
    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(
        Result<T1> first, Result<T2> second, Result<T3> third)
    {
        if (first.IsFailure) return Result<(T1, T2, T3)>.Failure(first.Error);
        if (second.IsFailure) return Result<(T1, T2, T3)>.Failure(second.Error);
        if (third.IsFailure) return Result<(T1, T2, T3)>.Failure(third.Error);
        return Result<(T1, T2, T3)>.Success((first.Value, second.Value, third.Value));
    }

    /// <summary>
    /// Combines multiple generic results, collecting ALL errors.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>A single successful result, or a failure with all collected errors.</returns>
    /// <exception cref="ResultException">Thrown when a result is uninitialized (<c>default</c>).</exception>
    public static Result CombineAll<T>(params ReadOnlySpan<Result<T>> results)
    {
        List<IError>? errors = null;
        foreach (var result in results)
        {
            if (!result.IsSuccess)
            {
                errors ??= [];
                errors.Add(result.Error); // throws ResultException on a default result
            }
        }

        return errors is null
            ? Success()
            : Failure(ErrorCollection.From(errors));
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

    /// <inheritdoc/>
    public override string ToString() => _tag switch
    {
        TagSuccess => "Result { Success }",
        TagFailure => $"Result {{ Failure: [{_error!.Code}] {_error.Message} }}",
        _ => "Result { Uninitialized }",
    };

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <summary>Implicitly converts an error to a failed <see cref="Result"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Implicitly converts an error collection to a failed <see cref="Result"/>.</summary>
    /// <param name="errors">The errors describing the failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is <see langword="null"/>.</exception>
    public static implicit operator Result(ErrorCollection errors) => Failure(errors);
}
