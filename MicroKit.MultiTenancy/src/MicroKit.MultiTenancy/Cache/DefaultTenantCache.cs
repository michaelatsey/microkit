using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace MicroKit.MultiTenancy.Cache;

/// <summary>In-process memory-cache implementation of <see cref="ITenantCache"/>.</summary>
public class DefaultTenantCache : ITenantCache
{
    private readonly IMemoryCache _memoryCache;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="memoryCache">The underlying memory cache.</param>
    public DefaultTenantCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    /// <inheritdoc/>
    public Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        _memoryCache.TryGetValue(key, out string? value);
        return Task.FromResult(value);
    }

    /// <inheritdoc/>
    public Task SetAsync(
        string key,
        string value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        _memoryCache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _memoryCache.Remove(key);
        return Task.CompletedTask;
    }
}
