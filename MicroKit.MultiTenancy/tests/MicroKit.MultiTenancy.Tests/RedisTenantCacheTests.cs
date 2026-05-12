using MicroKit.MultiTenancy.Redis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;

namespace MicroKit.MultiTenancy.Tests;

public sealed class RedisTenantCacheTests : IDisposable
{
    private readonly Mock<IDistributedCache> _distributedMock = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
    private readonly RedisTenantCache _cache;

    public RedisTenantCacheTests() =>
        _cache = new RedisTenantCache(_distributedMock.Object, _memoryCache);

    public void Dispose() => _memoryCache.Dispose();

    [Fact]
    public async Task GetAsync_L1Hit_DoesNotCallDistributed()
    {
        // Seed L1 directly
        _memoryCache.Set("tenant-1", "acme", TimeSpan.FromMinutes(5));

        var result = await _cache.GetAsync("tenant-1");

        Assert.Equal("acme", result);
        _distributedMock.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_L1Miss_FallsBackToDistributed()
    {
        _distributedMock
            .Setup(d => d.GetAsync("tenant-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("contoso"));

        var result = await _cache.GetAsync("tenant-2");

        Assert.Equal("contoso", result);
    }

    [Fact]
    public async Task GetAsync_BothMiss_ReturnsNull()
    {
        _distributedMock
            .Setup(d => d.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _cache.GetAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_WritesToBothCaches()
    {
        await _cache.SetAsync("tenant-3", "fabrikam", TimeSpan.FromMinutes(10));

        _distributedMock.Verify(
            d => d.SetAsync(
                "tenant-3",
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(o =>
                    o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(10)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // L1 should be warm now
        _memoryCache.TryGetValue("tenant-3", out string? l1);
        Assert.Equal("fabrikam", l1);
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromBothCaches()
    {
        // Prime L1
        _memoryCache.Set("tenant-4", "acme", TimeSpan.FromMinutes(5));

        await _cache.RemoveAsync("tenant-4");

        _distributedMock.Verify(
            d => d.RemoveAsync("tenant-4", It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.False(_memoryCache.TryGetValue("tenant-4", out _));
    }
}
