using MicroKit.Resilience.Abstractions;

namespace MicroKit.Resilience;

/// <summary>
/// Composite detector that aggregates multiple resilience strategy detectors.
/// </summary>
/// <remarks>
/// This detector combines the capabilities of multiple specialized detectors (SQL, HTTP, etc.)
/// and determines if a given exception should be retried by checking whether any detector
/// can handle it and considers it transient.
/// </remarks>
public sealed class CompositeResilienceDetector : IResilienceErrorDetector
{
    private readonly IEnumerable<IResilienceStrategyDetector> _detectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeResilienceDetector"/> class.
    /// </summary>
    /// <param name="detectors">The collection of resilience strategy detectors to composite.</param>
    public CompositeResilienceDetector(IEnumerable<IResilienceStrategyDetector> detectors)
    {
        _detectors = detectors ?? throw new ArgumentNullException(nameof(detectors));
    }

    /// <summary>
    /// Determines whether an exception should be retried based on registered detectors.
    /// </summary>
    /// <param name="ex">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if any detector can handle the exception and determines it is transient; otherwise, <c>false</c>.
    /// </returns>
    public bool ShouldRetry(Exception ex)
        => _detectors.Any(d => d.CanHandle(ex) && d.ShouldRetry(ex));
}
