namespace MicroKit.Resilience.Abstractions;

/// <summary>
/// Defines a contract for detecting whether a given exception can be handled by
/// a specific resilience strategy.
/// </summary>
/// <remarks>
/// Implementations should detect exceptions from specific systems (databases, HTTP services, etc.)
/// and determine whether they are transient errors worthy of retry.
/// </remarks>
public interface IResilienceStrategyDetector : IResilienceErrorDetector
{
    /// <summary>
    /// Determines whether the specified exception can be handled by this resilience strategy.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if this strategy recognizes and can handle the exception; otherwise, <c>false</c>.
    /// </returns>
    bool CanHandle(Exception ex);
}
