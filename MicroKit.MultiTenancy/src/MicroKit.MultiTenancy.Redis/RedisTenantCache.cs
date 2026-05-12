using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Redis;

/// <summary>
/// Two-layer tenant cache: L1 is an in-process <see cref="IMemoryCache"/> and L2 is a Redis-backed
/// <see cref="IDistributedCache"/>. The L1 TTL is configurable via <see cref="RedisTenantCacheOptions"/>.
/// </summary>
public class RedisTenantCache : ITenantCache
{
    private readonly IDistributedCache _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly TimeSpan _l1Ttl;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisTenantCache"/>.
    /// </summary>
    public RedisTenantCache(
        IDistributedCache distributedCache,
        IMemoryCache memoryCache,
        IOptions<RedisTenantCacheOptions> options)
    {
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
        _l1Ttl = options.Value.L1Ttl;
    }

    /// <inheritdoc/>
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
            _memoryCache.Set(key, distributed, _l1Ttl);
        }

        return distributed;
    }

    /// <inheritdoc/>
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

        _memoryCache.Set(key, value, _l1Ttl);
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _distributedCache.RemoveAsync(key, cancellationToken);
        _memoryCache.Remove(key);
    }
}
