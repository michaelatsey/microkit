namespace MicroKit.Caching.Abstractions;

/// <summary>
/// Controls how a value is stored in the cache.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>Gets or sets the cache entry lifetime. Defaults to the implementation's default when <see langword="null"/>.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Gets or sets whether to skip cache reads and writes for this operation.</summary>
    public bool BypassCache { get; set; } = false;

    /// <summary>Gets or sets whether to use sliding expiration instead of absolute expiration.</summary>
    public bool SlidingExpiration { get; set; } = false;

    /// <summary>
    /// Initialises a new <see cref="CacheOptions"/> instance.
    /// </summary>
    public CacheOptions(TimeSpan? duration = null, bool bypassCache = false, bool slidingExpiration = false)
    {
        Duration = duration;
        BypassCache = bypassCache;
        SlidingExpiration = slidingExpiration;
    }
}
