namespace MicroKit.Security.Abstractions.Options;

/// <summary>
/// Configuration options for security caching.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// Enable caching of validation results.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default cache duration in seconds.
    /// </summary>
    public int DefaultDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Cache duration for successful validations in seconds.
    /// </summary>
    public int SuccessDurationSeconds { get; set; } = 600;

    /// <summary>
    /// Cache duration for failed validations in seconds (shorter to allow retry).
    /// </summary>
    public int FailureDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of cached entries.
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;

    /// <summary>
    /// Cache key prefix for namespacing.
    /// </summary>
    public string KeyPrefix { get; set; } = "microkit:security:";

    /// <summary>
    /// Enable sliding expiration for cache entries.
    /// </summary>
    public bool UseSlidingExpiration { get; set; } = true;
}
