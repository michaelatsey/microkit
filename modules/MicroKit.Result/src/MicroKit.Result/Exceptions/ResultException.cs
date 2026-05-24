namespace MicroKit.Result;

/// <summary>
/// Thrown when accessing <see cref="Result{T}.Value"/> on a failure result
/// or <see cref="Result{T}.Error"/> on a success result.
/// </summary>
public sealed class ResultException : InvalidOperationException
{
    /// <summary>Gets the errors that caused this exception, if any.</summary>
    public IReadOnlyList<IError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ResultException(string message) : base(message)
    {
        Errors = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ResultException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultException"/> class with the contributing errors.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errors">The errors that caused this exception.</param>
    public ResultException(string message, IReadOnlyList<IError> errors)
        : base(message)
    {
        Errors = errors ?? [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultException"/> class with an inner exception and contributing errors.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="errors">The errors that caused this exception.</param>
    public ResultException(string message, Exception innerException, IReadOnlyList<IError> errors)
        : base(message, innerException)
    {
        Errors = errors ?? [];
    }
}
