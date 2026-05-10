namespace MicroKit.Domain.Primitives;

/// <summary>
/// Represents the outcome of a domain operation that produces no value.
/// Use <see cref="Result{T}"/> when the operation yields a value on success.
/// </summary>
public class Result
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the error associated with a failed result. Returns <see cref="Error.None"/> on success.</summary>
    public Error Error { get; }

    /// <summary>Initializes a new instance of <see cref="Result"/>.</summary>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("A successful result must not carry an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("A failed result must carry an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result with the given <paramref name="error"/>.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful <see cref="Result{T}"/> carrying <paramref name="value"/>.</summary>
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    /// <summary>Creates a failed <see cref="Result{T}"/> with the given <paramref name="error"/>.</summary>
    public static Result<T> Failure<T>(Error error) => new(default!, false, error);
}

/// <summary>
/// Represents the outcome of a domain operation that produces a value of type <typeparamref name="T"/>
/// on success.
/// </summary>
/// <typeparam name="T">The type of the value returned on success.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T _value;

    internal Result(T value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value produced by the operation.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException($"Cannot access the value of a failed result. Error: {Error}");

    /// <summary>Implicitly converts a value to a successful <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(T value) => Result.Success(value);

    /// <summary>Implicitly converts an <see cref="Error"/> to a failed <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(Error error) => Result.Failure<T>(error);
}
