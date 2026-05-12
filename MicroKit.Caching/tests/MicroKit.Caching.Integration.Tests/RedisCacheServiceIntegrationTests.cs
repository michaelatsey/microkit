using DotNet.Testcontainers.Builders;
using MicroKit.Caching.Abstractions;
using MicroKit.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;

namespace MicroKit.Caching.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="DistributedCacheService"/> against a real Redis instance
/// started via Testcontainers. Requires Docker to be running.
/// </summary>
public sealed class RedisCacheServiceIntegrationTests : IAsyncLifetime
{
    private sealed record Product(string Name, decimal Price);

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ICacheService _service = null!;
    private IServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();

        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = _redis.GetConnectionString();
        });
        services.AddOptions<DistributedCacheOptions>();
        services.AddSingleton<ICacheService, DistributedCacheService>();

        _serviceProvider = services.BuildServiceProvider();
        _service = _serviceProvider.GetRequiredService<ICacheService>();
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        if (_serviceProvider is IAsyncDisposable ad)
            await ad.DisposeAsync();
    }

    [Fact]
    public async Task SetAndGet_RoundTrip_ReturnsOriginalValue()
    {
        var product = new Product("Widget", 19.99m);

        await _service.SetAsync("p1", product, new CacheOptions(TimeSpan.FromMinutes(5)));
        var result = await _service.GetAsync<Product>("p1");

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
        Assert.Equal(19.99m, result.Price);
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var result = await _service.GetAsync<Product>("non-existent");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_DeletesKey()
    {
        await _service.SetAsync("p2", new Product("Gadget", 49.99m));

        await _service.RemoveAsync("p2");

        Assert.Null(await _service.GetAsync<Product>("p2"));
    }

    [Fact]
    public async Task SetAsync_SlidingExpiration_ValueIsAccessible()
    {
        var opts = new CacheOptions(TimeSpan.FromSeconds(30), slidingExpiration: true);

        await _service.SetAsync("p3", new Product("Doohickey", 5.00m), opts);
        var result = await _service.GetAsync<Product>("p3");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task OverwriteExistingKey_ReturnsUpdatedValue()
    {
        await _service.SetAsync("p4", new Product("Old", 1.00m));
        await _service.SetAsync("p4", new Product("New", 2.00m));

        var result = await _service.GetAsync<Product>("p4");

        Assert.Equal("New", result!.Name);
    }
}
