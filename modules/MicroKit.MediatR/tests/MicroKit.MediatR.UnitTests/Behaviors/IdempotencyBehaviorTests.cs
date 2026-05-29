using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.Result;
using static MicroKit.Result.Result;
using NSubstitute;
using Shouldly;
using Xunit;
using MicroKit.MediatR.Behaviors.Idempotency;
using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class IdempotencyBehaviorTests
{
    [Fact]
    public async Task Handle_WhenMarkerAbsent_PassesThrough()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("result"); };
        var store = Substitute.For<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<NonIdempotentRequest, string>(store);

        var result = await behavior.Handle(new NonIdempotentRequest(), next, CancellationToken.None);

        result.ShouldBe("result");
        callCount.ShouldBe(1);
        await store.DidNotReceive().GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenIdempotencyKeyIsNull_ThrowsInvalidOperationException()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var store = Substitute.For<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<NullKeyRequest, string>(store);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new NullKeyRequest(), next, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenIdempotencyKeyIsEmpty_ThrowsInvalidOperationException()
    {
        RequestHandlerDelegate<string> next = () => Task.FromResult("result");
        var store = Substitute.For<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<EmptyKeyRequest, string>(store);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new EmptyKeyRequest(), next, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ReturnsCachedResponseWithoutCallingNext()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("fresh"); };
        var store = Substitute.For<IIdempotencyStore>();
        store.GetAsync<string>("idem-key", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CacheEntry<string>?>(new CacheEntry<string>("cached")));
        var behavior = new IdempotencyBehavior<CacheHitRequest, string>(store);

        var result = await behavior.Handle(new CacheHitRequest(), next, CancellationToken.None);

        result.ShouldBe("cached");
        callCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_CallsNextAndStoresResult()
    {
        var callCount = 0;
        RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("fresh"); };
        var store = Substitute.For<IIdempotencyStore>();
        store.GetAsync<string>("idem-key", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<CacheEntry<string>?>(null));
        store.SetAsync("idem-key", "fresh", Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        var behavior = new IdempotencyBehavior<CacheMissRequest, string>(store);

        var result = await behavior.Handle(new CacheMissRequest(), next, CancellationToken.None);

        result.ShouldBe("fresh");
        callCount.ShouldBe(1);
        await store.Received(1).SetAsync("idem-key", "fresh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenResponseIsResultSuccess_StoresIt()
    {
        var callCount = 0;
        var success = Success("data");
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(success); };
        // Concrete store: NSubstitute default for ValueTask<Result<string>?> returns a zero-initialized
        // struct (not null), so the behavior would incorrectly treat it as a cache hit. A concrete
        // implementation that correctly returns default(TResponse?) = null avoids this.
        var store = new TrackingStore();
        var behavior = new IdempotencyBehavior<IdempotentSuccessRequest, Result<string>>(store);

        var result = await behavior.Handle(new IdempotentSuccessRequest(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        callCount.ShouldBe(1);
        store.WasStored.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenResponseIsResultFailure_DoesNotStoreIt()
    {
        var callCount = 0;
        var failure = Failure<string>(new TestError());
        RequestHandlerDelegate<Result<string>> next = () =>
            { callCount++; return Task.FromResult(failure); };
        var store = new TrackingStore();
        var behavior = new IdempotencyBehavior<IdempotentFailureRequest, Result<string>>(store);

        var result = await behavior.Handle(new IdempotentFailureRequest(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        callCount.ShouldBe(1);
        store.WasStored.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenCacheHit_DoesNotCallSetAsync()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.GetAsync<string>("idem-key", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CacheEntry<string>?>(new CacheEntry<string>("cached")));
        var behavior = new IdempotencyBehavior<CacheHitNoStoreRequest, string>(store);

        await behavior.Handle(new CacheHitNoStoreRequest(), () => Task.FromResult("fresh"), CancellationToken.None);

        _ = store.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private sealed record NonIdempotentRequest;

    private sealed record NullKeyRequest : IIdempotentCommand
    {
        public string IdempotencyKey => null!;
    }

    private sealed record EmptyKeyRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "";
    }

    private sealed record CacheHitRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "idem-key";
    }

    private sealed record CacheMissRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "idem-key";
    }

    private sealed record IdempotentSuccessRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "idem-key";
    }

    private sealed record IdempotentFailureRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "idem-key";
    }

    private sealed record CacheHitNoStoreRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "idem-key";
    }

    [Fact]
    public async Task Handle_WhenNextThrows_PropagatesExceptionWithoutStoringResult()
    {
        var store = new TrackingStore();
        RequestHandlerDelegate<string> next = () =>
            Task.FromException<string>(new InvalidOperationException("handler exploded"));
        var behavior = new IdempotencyBehavior<IdempotentThrowingRequest, string>(store);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new IdempotentThrowingRequest(), next, CancellationToken.None));

        store.WasStored.ShouldBeFalse();
    }

    // Error is abstract — a concrete subtype is required to construct Result.Failure.
    private sealed record TestError() : Error(ErrorCode.From("TEST.ERROR"), "test error");

    private sealed record IdempotentThrowingRequest : IIdempotentCommand
    {
        public string IdempotencyKey => "idem-key";
    }

    // Concrete IIdempotencyStore used for tests that need to verify whether SetAsync was called.
    // Returns null (miss) from GetAsync so the behavior always calls next() and reaches the SetAsync guard.
    // CacheEntry<TResponse>? is a reference type — null is unambiguously a miss for any TResponse.
    private sealed class TrackingStore : IIdempotencyStore
    {
        public bool WasStored { get; private set; }

        public ValueTask<CacheEntry<TResponse>?> GetAsync<TResponse>(string key, CancellationToken ct = default)
            => ValueTask.FromResult<CacheEntry<TResponse>?>(null);

        public ValueTask SetAsync<TResponse>(string key, TResponse response, CancellationToken ct = default)
        {
            WasStored = true;
            return ValueTask.CompletedTask;
        }
    }
}
