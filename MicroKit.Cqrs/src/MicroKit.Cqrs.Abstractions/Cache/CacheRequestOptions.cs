namespace MicroKit.Cqrs.Abstractions.Cache;

/// <summary>Per-request caching preferences, independent of any caching infrastructure.</summary>
public sealed record CacheRequestOptions
{
    /// <summary>When <see langword="true"/>, the pipeline skips the cache entirely for this request.</summary>
    public bool BypassCache { get; init; }

    /// <summary>When <see langword="true"/>, the cache entry uses sliding expiration; otherwise absolute.</summary>
    public bool SlidingExpiration { get; init; }
}
