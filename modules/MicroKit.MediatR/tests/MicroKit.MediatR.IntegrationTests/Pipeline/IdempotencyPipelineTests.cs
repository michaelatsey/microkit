using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.Behaviors.Idempotency;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using MicroKit.Result;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Pipeline;

/// <summary>
/// Verifies IdempotencyBehavior integration with the real MediatR pipeline.
/// Cache miss: handler is called and response is stored.
/// Cache hit: stored response is returned without calling the handler.
/// </summary>
public sealed class IdempotencyPipelineTests
{
    private static ServiceProvider BuildPipeline(AttemptCounter counter)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new DomainEventLog());
        services.AddSingleton(counter);

        // Register an in-memory IIdempotencyStore BEFORE AddIdempotencyBehavior so that
        // TryAdd does not override it with DistributedCacheIdempotencyStore.
        var store = new InMemoryIdempotencyStore();
        services.AddSingleton<IIdempotencyStore>(store);

        // IOptions<JsonSerializerOptions> is required by behaviors — register with defaults.
        services.AddSingleton(Options.Create(new System.Text.Json.JsonSerializerOptions()));

        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<EchoCommand>()
            .AddLoggingBehavior()
            .AddIdempotencyBehavior());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_CallsHandlerAndStoresResponse()
    {
        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();
        var command = new IdempotentEchoCommand("key-miss", "hello");

        var result = await mediator.SendCommandAsync<IdempotentEchoCommand, Result<string>>(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
        counter.Count.ShouldBe(1, "handler must be called once on a cache miss");
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ReturnsCachedResponseWithoutCallingHandler()
    {
        var counter = new AttemptCounter();
        using var provider = BuildPipeline(counter);
        var mediator = provider.GetRequiredService<IMediator>();
        var command = new IdempotentEchoCommand("key-hit", "from-cache");

        // First dispatch — populates the cache.
        var first = await mediator.SendCommandAsync<IdempotentEchoCommand, Result<string>>(command);
        first.IsSuccess.ShouldBeTrue();
        counter.Count.ShouldBe(1);

        // Second dispatch with the SAME IdempotencyKey — must return cached value, handler NOT called.
        var second = await mediator.SendCommandAsync<IdempotentEchoCommand, Result<string>>(command);

        second.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe("from-cache");
        counter.Count.ShouldBe(1, "handler must NOT be called again on a cache hit");
    }

    // ── In-memory idempotency store ────────────────────────────────────────

    // Stores CacheEntry<TResponse> objects so GetAsync can return the unambiguous null/not-null
    // hit/miss signal regardless of TResponse type (including value structs and Result<T>).
    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, object?> _store = [];

        public ValueTask<CacheEntry<TResponse>?> GetAsync<TResponse>(string key, CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var val) && val is CacheEntry<TResponse> entry)
                return ValueTask.FromResult<CacheEntry<TResponse>?>(entry);
            return ValueTask.FromResult<CacheEntry<TResponse>?>(null);
        }

        public ValueTask SetAsync<TResponse>(string key, TResponse response, CancellationToken ct = default)
        {
            _store[key] = new CacheEntry<TResponse>(response);
            return ValueTask.CompletedTask;
        }
    }
}
