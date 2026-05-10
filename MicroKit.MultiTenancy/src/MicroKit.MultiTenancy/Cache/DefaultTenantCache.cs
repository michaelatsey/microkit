using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace MicroKit.MultiTenancy.Cache;

public class DefaultTenantCache : ITenantCache
{
    private readonly IMemoryCache _memoryCache;

    public DefaultTenantCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        _memoryCache.TryGetValue(key, out string? value);
        return Task.FromResult(value);
    }

    public Task SetAsync(
        string key,
        string value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        _memoryCache.Set(key, value, ttl);
        return Task.CompletedTask;
    }
}
