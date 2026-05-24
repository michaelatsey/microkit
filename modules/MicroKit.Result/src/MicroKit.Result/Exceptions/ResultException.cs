namespace MicroKit.Result;

/// <summary>
/// Thrown when accessing <see cref="Result{T}.Value"/> on a failure result
/// or <see cref="Result{T}.Error"/> on a success result.
/// </summary>
public sealed class ResultException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResultException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ResultException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ResultException(string message, Exception innerException)
        : base(message, innerException) { }
}
