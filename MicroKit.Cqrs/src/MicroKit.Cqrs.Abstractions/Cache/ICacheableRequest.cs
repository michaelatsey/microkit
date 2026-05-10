namespace MicroKit.Cqrs.Abstractions.Cache;

/// <summary>Marks a query as cacheable and exposes its caching parameters.</summary>
public interface ICacheableRequest
{
    /// <summary>Gets the logical cache key for this request.</summary>
    string CacheKey { get; }

    /// <summary>Gets the time-to-live for the cached result, or <see langword="null"/> to use the pipeline default.</summary>
    TimeSpan? CacheDuration { get; }

    /// <summary>Gets per-request cache options such as bypass or sliding-expiration flags.</summary>
    CacheRequestOptions? Options { get; }
}
