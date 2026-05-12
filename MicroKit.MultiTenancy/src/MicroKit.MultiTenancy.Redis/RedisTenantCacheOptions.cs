namespace MicroKit.MultiTenancy.Redis;

/// <summary>
/// Configuration options for <see cref="RedisTenantCache"/>.
/// </summary>
public sealed class RedisTenantCacheOptions
{
    /// <summary>
    /// Gets or sets the time-to-live for the in-process L1 memory cache layer.
    /// Entries fetched from Redis are held in memory for this duration to avoid
    /// repeated network calls for the same tenant within a single request window.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan L1Ttl { get; set; } = TimeSpan.FromMinutes(5);
}
