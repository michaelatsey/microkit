namespace MicroKit.Resilience.Abstractions;

/// <summary>
/// Defines a contract for detecting whether an exception represents a transient error
/// that should be retried.
/// </summary>
public interface IResilienceErrorDetector
{
    /// <summary>
    /// Determines whether the specified exception is transient and can be safely retried.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception is transient and should be retried; otherwise, <c>false</c>.
    /// </returns>
    bool ShouldRetry(Exception ex);
}
