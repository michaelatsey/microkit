using MicroKit.Caching.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MicroKit.Caching.Distributed;

public class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _distributedCache;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DistributedCacheService(IDistributedCache distributedCache)
    {
        _distributedCache = distributedCache;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var json = await _distributedCache.GetStringAsync(key, cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }


    public async Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);

        var distributedOptions = new DistributedCacheEntryOptions();
        var duration = options?.Duration ?? TimeSpan.FromMinutes(30);

        if (options is not null && options.SlidingExpiration)
        {
            distributedOptions.SlidingExpiration = duration;
        }
        else
        {
            distributedOptions.AbsoluteExpirationRelativeToNow = duration;
        }

        await _distributedCache.SetStringAsync(key, json, distributedOptions, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _distributedCache.RemoveAsync(key, cancellationToken);
    }
}
