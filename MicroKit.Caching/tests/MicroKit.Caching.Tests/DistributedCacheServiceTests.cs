using MicroKit.Caching.Abstractions;
using MicroKit.Caching.Distributed;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using System.Text.Json;

namespace MicroKit.Caching.Tests;

public sealed class DistributedCacheServiceTests
{
    private sealed record Payload(string Name, int Value);

    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly ICacheService _service;

    public DistributedCacheServiceTests()
    {
        var options = Options.Create(new DistributedCacheOptions());
        _service = new DistributedCacheService(_cacheMock.Object, options);
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_KeyFound_DeserializesValue()
    {
        var payload = new Payload("test", 42);
        var json = JsonSerializer.Serialize(payload);
        _cacheMock.Setup(c => c.GetAsync("key", default))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        var result = await _service.GetAsync<Payload>("key");

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_KeyMissing_ReturnsNull()
    {
        _cacheMock.Setup(c => c.GetAsync("missing", default))
            .ReturnsAsync((byte[]?)null);

        var result = await _service.GetAsync<Payload>("missing");

        Assert.Null(result);
    }

    // ── SetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_DefaultOptions_UsesAbsoluteExpiration30Min()
    {
        await _service.SetAsync("key", new Payload("x", 1));

        _cacheMock.Verify(c => c.SetAsync(
            "key",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(30)),
            default),
            Times.Once);
    }

    [Fact]
    public async Task SetAsync_CustomDuration_UsesProvidedDuration()
    {
        var opts = new CacheOptions(duration: TimeSpan.FromHours(1));

        await _service.SetAsync("key", new Payload("x", 1), opts);

        _cacheMock.Verify(c => c.SetAsync(
            "key",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromHours(1)),
            default),
            Times.Once);
    }

    [Fact]
    public async Task SetAsync_SlidingExpiration_UsesSlidingExpiration()
    {
        var opts = new CacheOptions(duration: TimeSpan.FromMinutes(10), slidingExpiration: true);

        await _service.SetAsync("key", new Payload("x", 1), opts);

        _cacheMock.Verify(c => c.SetAsync(
            "key",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.SlidingExpiration == TimeSpan.FromMinutes(10) &&
                o.AbsoluteExpirationRelativeToNow == null),
            default),
            Times.Once);
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_CallsDistributedCacheRemove()
    {
        await _service.RemoveAsync("key");

        _cacheMock.Verify(c => c.RemoveAsync("key", default), Times.Once);
    }

    // ── Custom serializer options ─────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_CaseInsensitive_DeserializesCorrectly()
    {
        // Server sends PascalCase, we read via case-insensitive options
        var json = """{"name":"hello","value":7}""";
        _cacheMock.Setup(c => c.GetAsync("key", default))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        var result = await _service.GetAsync<Payload>("key");

        Assert.Equal("hello", result!.Name);
    }

    [Fact]
    public async Task CustomSerializerOptions_AreUsedForSerialization()
    {
        var customOptions = new DistributedCacheOptions
        {
            SerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        };
        var svc = new DistributedCacheService(
            _cacheMock.Object,
            Options.Create(customOptions));

        await svc.SetAsync("key", new Payload("abc", 5));

        _cacheMock.Verify(c => c.SetAsync(
            "key",
            It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("\"name\"")),
            It.IsAny<DistributedCacheEntryOptions>(),
            default),
            Times.Once);
    }
}
