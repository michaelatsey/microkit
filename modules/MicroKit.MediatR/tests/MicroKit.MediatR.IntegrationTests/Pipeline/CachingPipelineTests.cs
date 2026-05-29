using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using MicroKit.Result;
using MicroKit.Result.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Pipeline;

/// <summary>
/// Verifies CachingBehavior integration with the real MediatR pipeline.
/// Cache miss: handler is called and result is serialized into the cache.
/// Cache hit: cached bytes are deserialized and returned without calling the handler.
/// </summary>
public sealed class CachingPipelineTests
{
    private static ServiceProvider BuildPipeline(AttemptCounter counter)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new DomainEventLog());
        services.AddSingleton(counter);

        // In-memory IDistributedCache for round-trip serialization in tests.
        services.AddSingleton<IDistributedCache, InMemoryDistributedCache>();

        // Result<T> requires the ResultJsonConverterFactory for round-trip serialization (ADR-007).
        services.AddSingleton(Options.Create(new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new ResultJsonConverterFactory() }
        }));

        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<EchoCommand>()
            .AddLoggingBehavior()
            .AddCachingBehavior());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_CallsHandlerAndCachesResult()
    {
        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new CacheableDoubleQuery("cache-miss-key", 5);

        var result = await mediator.SendQueryAsync<CacheableDoubleQuery, Result<int>>(query);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(10);
        counter.Count.ShouldBe(1, "handler must be called once on a cache miss");
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ReturnsDeserializedValueWithoutCallingHandler()
    {
        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new CacheableDoubleQuery("cache-hit-key", 7);

        // First dispatch — populates the cache.
        var first = await mediator.SendQueryAsync<CacheableDoubleQuery, Result<int>>(query);
        first.IsSuccess.ShouldBeTrue();
        first.Value.ShouldBe(14);
        counter.Count.ShouldBe(1);

        // Second dispatch with the SAME CacheKey — must return deserialized cached value.
        var second = await mediator.SendQueryAsync<CacheableDoubleQuery, Result<int>>(query);

        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(14);
        counter.Count.ShouldBe(1, "handler must NOT be called again on a cache hit");
    }

    // ── In-memory distributed cache ────────────────────────────────────────

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = [];

        public byte[]? Get(string key)
            => _store.TryGetValue(key, out var val) ? val : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _store[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
    }
}
