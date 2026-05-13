namespace MicroKit.Resilience.Builder;

/// <summary>
/// Configuration options for resilience retry and circuit breaker policies.
/// </summary>
/// <remarks>
/// This class defines the behavior of retry strategies and circuit breaker patterns
/// applied to resilience pipelines. All settings support sensible defaults suitable
/// for most production scenarios.
/// </remarks>
public sealed class ResilienceRetryOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts before failure propagation.
    /// Default: 3.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the unique name of the resilience pipeline.
    /// Default: "DefaultRetry".
    /// </summary>
    public string PipelineName { get; set; } = "DefaultRetry";

    /// <summary>
    /// Gets or sets the initial delay in seconds between retry attempts, used as the base
    /// for exponential backoff calculations.
    /// Default: 1.0 second.
    /// </summary>
    public double BaseDelaySeconds { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets a value indicating whether fallback handling is enabled.
    /// Default: true.
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether circuit breaker protection is enabled.
    /// Default: true.
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Gets or sets the failure ratio threshold (0.0-1.0) that triggers circuit breaker opening.
    /// For example, 0.5 means 50% failure rate triggers the circuit breaker.
    /// Default: 0.5 (50%).
    /// </summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the minimum number of calls required before evaluating the failure ratio.
    /// The circuit breaker will not open until at least this many calls have been recorded.
    /// Default: 10.
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Gets or sets the duration for which the circuit breaker remains open after being triggered.
    /// During this time, all requests will fail fast without attempting the protected operation.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

}

