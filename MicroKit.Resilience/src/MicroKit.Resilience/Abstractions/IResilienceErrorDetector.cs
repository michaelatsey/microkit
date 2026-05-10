namespace MicroKit.Resilience.Abstractions;

/// <summary>
/// 
/// </summary>
public interface IResilienceErrorDetector
{
    /// <summary>
    /// Determines whether the specified ex is transient.
    /// </summary>
    /// <param name="ex">The ex.</param>
    /// <returns>
    ///   <c>true</c> if the specified ex is transient; otherwise, <c>false</c>.
    /// </returns>
    bool ShouldRetry(Exception ex);
}

