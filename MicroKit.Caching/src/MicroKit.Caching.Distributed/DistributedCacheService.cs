using MicroKit.Caching.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace MicroKit.Caching.Distributed;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by <see cref="IDistributedCache"/>.
/// </summary>
public sealed class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _distributedCache;
    private readonly JsonSerializerOptions _serializerOptions;

    public DistributedCacheService(
        IDistributedCache distributedCache,
        IOptions<DistributedCacheOptions> options)
    {
        _distributedCache = distributedCache;
        _serializerOptions = options.Value.SerializerOptions;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await _distributedCache.GetStringAsync(key, cancellationToken);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, _serializerOptions);
    }

    public async Task SetAsync<T>(string key, T value, CacheOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        var json = JsonSerializer.Serialize(value, _serializerOptions);

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
