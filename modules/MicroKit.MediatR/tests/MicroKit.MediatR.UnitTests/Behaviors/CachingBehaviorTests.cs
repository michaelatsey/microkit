using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.Result;
using MicroKit.Result.Serialization;
using static MicroKit.Result.Result;
using NSubstitute;
using Shouldly;
using Xunit;
using MicroKit.MediatR.Behaviors.Errors;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class CachingBehaviorTests
{
    private static IOptions<JsonSerializerOptions> DefaultJsonOptions =>
        Options.Create(new JsonSerializerOptions());

    // Required for Result<T> round-trip per ADR-007.
    private static IOptions<JsonSerializerOptions> ResultJsonOptions =>
        Options.Create(new JsonSerializerOptions
        {
            Converters = { new ResultJsonConverterFactory() }
        });

    [Fact]
    public async Task Handle_WhenMarkerAbsent_PassesThrough()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<NonCacheableQuery, string>(
            cache, DefaultJsonOptions, NullLogger<CachingBehavior<NonCacheableQuery, string>>.Instance);

        var result = await behavior.Handle(new NonCacheableQuery(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ReturnsDeserializedValueWithoutCallingNext()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("fresh"); };
        var serialized = JsonSerializer.SerializeToUtf8Bytes("cached");
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(serialized));
        var behavior = new CachingBehavior<CacheableQueryHit, string>(
            cache, DefaultJsonOptions, NullLogger<CachingBehavior<CacheableQueryHit, string>>.Instance);

        var result = await behavior.Handle(new CacheableQueryHit(), next, CancellationToken.None);

        result.ShouldBe("cached");
        callCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_CallsNextAndStoresResult()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("fresh"); };
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var behavior = new CachingBehavior<CacheableQueryMiss, string>(
            cache, DefaultJsonOptions, NullLogger<CachingBehavior<CacheableQueryMiss, string>>.Instance);

        var result = await behavior.Handle(new CacheableQueryMiss(), next, CancellationToken.None);

        result.ShouldBe("fresh");
        callCount.ShouldBe(1);
        await cache.Received(1).SetAsync(
            "cache-key", Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExpiryIsNull_StoresWithoutExpiry()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var behavior = new CachingBehavior<CacheableQueryNullExpiry, string>(
            cache, DefaultJsonOptions, NullLogger<CachingBehavior<CacheableQueryNullExpiry, string>>.Instance);

        var result = await behavior.Handle(new CacheableQueryNullExpiry(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
        // Stored without expiry — AbsoluteExpirationRelativeToNow should be null
        await cache.Received(1).SetAsync(
            "cache-key", Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenResponseIsResultSuccess_CachesIt()
    {
        var callCount = 0;
        var success = Success("data");
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(success); };
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var behavior = new CachingBehavior<CacheableQuerySuccess, Result<string>>(
            cache, ResultJsonOptions,
            NullLogger<CachingBehavior<CacheableQuerySuccess, Result<string>>>.Instance);

        var result = await behavior.Handle(new CacheableQuerySuccess(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        callCount.ShouldBe(1);
        await cache.Received(1).SetAsync(
            "cache-key", Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCacheHitButDeserializationFails_ReturnsFailureForResultResponse()
    {
        var corruptBytes = "not-valid-json"u8.ToArray();
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(corruptBytes));
        var behavior = new CachingBehavior<CacheableQueryFailure, Result<string>>(
            cache, ResultJsonOptions,
            NullLogger<CachingBehavior<CacheableQueryFailure, Result<string>>>.Instance);

        var result = await behavior.Handle(new CacheableQueryFailure(), () => Task.FromResult(Success("fresh")), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CacheDeserializationError>();
        var error = (CacheDeserializationError)result.Error;
        error.CacheKey.ShouldBe("cache-key");
    }

    [Fact]
    public async Task Handle_WhenCacheHitButDeserializationFails_ThrowsForDirectResponse()
    {
        var corruptBytes = "not-valid-json"u8.ToArray();
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(corruptBytes));
        var behavior = new CachingBehavior<CacheableQueryHit, string>(
            cache, DefaultJsonOptions,
            NullLogger<CachingBehavior<CacheableQueryHit, string>>.Instance);

        // For direct (non-Result<T>) responses, the deserialization failure propagates as
        // an InvalidOperationException with the JsonException as inner cause.
        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new CacheableQueryHit(), () => Task.FromResult("fresh"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenResponseIsResultFailure_DoesNotCacheIt()
    {
        var callCount = 0;
        var failure = Failure<string>(new TestError());
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(failure); };
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        var behavior = new CachingBehavior<CacheableQueryFailure, Result<string>>(
            cache, ResultJsonOptions,
            NullLogger<CachingBehavior<CacheableQueryFailure, Result<string>>>.Instance);

        var result = await behavior.Handle(new CacheableQueryFailure(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(1);
        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCacheHit_DoesNotCallSetAsync()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(JsonSerializer.SerializeToUtf8Bytes("cached")));
        var behavior = new CachingBehavior<CacheHitNoStoreQuery, string>(
            cache, DefaultJsonOptions, NullLogger<CachingBehavior<CacheHitNoStoreQuery, string>>.Instance);

        await behavior.Handle(new CacheHitNoStoreQuery(), () => Task.FromResult("fresh"), CancellationToken.None);

        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    private sealed record NonCacheableQuery;

    private sealed record CacheableQueryHit : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }

    private sealed record CacheableQueryMiss : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }

    private sealed record CacheableQueryNullExpiry : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => null;
    }

    private sealed record CacheableQuerySuccess : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }

    private sealed record CacheableQueryFailure : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }

    private sealed record CacheHitNoStoreQuery : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }

    [Fact]
    public async Task Handle_WhenNextThrows_PropagatesExceptionWithoutCachingResult()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync("cache-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        RequestHandlerDelegate<string> next = () =>
            Task.FromException<string>(new InvalidOperationException("handler exploded"));
        var behavior = new CachingBehavior<CacheableQueryThrow, string>(
            cache, DefaultJsonOptions, NullLogger<CachingBehavior<CacheableQueryThrow, string>>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new CacheableQueryThrow(), next, CancellationToken.None));

        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    // Error is abstract — a concrete subtype is required to construct Result.Failure.
    private sealed record TestError() : Error(ErrorCode.From("TEST.ERROR"), "test error");

    private sealed record CacheableQueryThrow : ICacheableQuery
    {
        public string CacheKey => "cache-key";
        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }
}
