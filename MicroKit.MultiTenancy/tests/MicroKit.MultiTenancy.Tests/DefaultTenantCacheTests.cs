using MicroKit.MultiTenancy.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Tests;

public sealed class DefaultTenantCacheTests : IDisposable
{
    private readonly IMemoryCache _memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
    private readonly DefaultTenantCache _cache;

    public DefaultTenantCacheTests() => _cache = new DefaultTenantCache(_memoryCache);

    public void Dispose() => _memoryCache.Dispose();

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsNull()
    {
        var result = await _cache.GetAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGet_ReturnsStoredValue()
    {
        await _cache.SetAsync("tenant-1", "acme", TimeSpan.FromMinutes(5));

        var result = await _cache.GetAsync("tenant-1");

        Assert.Equal("acme", result);
    }

    [Fact]
    public async Task RemoveAsync_ExistingKey_SubsequentGetReturnsNull()
    {
        await _cache.SetAsync("tenant-2", "contoso", TimeSpan.FromMinutes(5));

        await _cache.RemoveAsync("tenant-2");

        Assert.Null(await _cache.GetAsync("tenant-2"));
    }

    [Fact]
    public async Task RemoveAsync_MissingKey_DoesNotThrow()
    {
        var ex = await Record.ExceptionAsync(() => _cache.RemoveAsync("non-existent"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingValue()
    {
        await _cache.SetAsync("key", "original", TimeSpan.FromMinutes(5));
        await _cache.SetAsync("key", "updated", TimeSpan.FromMinutes(5));

        var result = await _cache.GetAsync("key");

        Assert.Equal("updated", result);
    }
}
