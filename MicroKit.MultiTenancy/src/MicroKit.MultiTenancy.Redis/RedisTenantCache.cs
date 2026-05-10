using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace MicroKit.MultiTenancy.Redis;

public class RedisTenantCache : ITenantCache
{
    private readonly IDistributedCache _distributedCache;
    private readonly IMemoryCache _memoryCache;

    public RedisTenantCache(
        IDistributedCache distributedCache,
        IMemoryCache memoryCache)
    {
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        // L1
        if (_memoryCache.TryGetValue(key, out string? local))
            return local;

        // L2
        var distributed = await _distributedCache
            .GetStringAsync(key, cancellationToken);

        if (distributed is not null)
        {
            _memoryCache.Set(key, distributed, TimeSpan.FromMinutes(5));
        }

        return distributed;
    }

    public async Task SetAsync(
        string key,
        string value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        await _distributedCache.SetStringAsync(
            key,
            value,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            },
            cancellationToken);

        _memoryCache.Set(key, value, TimeSpan.FromMinutes(5));
    }
}
