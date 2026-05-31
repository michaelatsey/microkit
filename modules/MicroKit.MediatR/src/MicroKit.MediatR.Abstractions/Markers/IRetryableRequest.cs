namespace MicroKit.MediatR;

/// <summary>
/// Opts a request into <c>RetryBehavior</c> (pipeline order 600).
/// Retries are applied via a Polly exponential back-off pipeline: <c>Delay</c>, <c>2×Delay</c>, <c>4×Delay</c>, …
/// </summary>
/// <example>
/// <code>
/// public sealed record FetchExternalDataQuery(string ResourceId)
///     : IQuery&lt;Result&lt;ExternalDataDto&gt;&gt;, IRetryableRequest
/// {
///     public int MaxRetries => 3;
///     public TimeSpan Delay => TimeSpan.FromSeconds(1);
/// }
/// </code>
/// </example>
public interface IRetryableRequest
{
    /// <summary>
    /// Maximum number of retry attempts. Must be greater than zero.
    /// </summary>
    int MaxRetries { get; }

    /// <summary>
    /// Base delay for the exponential back-off strategy. The actual wait between attempts is
    /// <c>Delay × 2^(attempt - 1)</c>. This is not a fixed per-retry delay.
    /// </summary>
    TimeSpan Delay { get; }
}
