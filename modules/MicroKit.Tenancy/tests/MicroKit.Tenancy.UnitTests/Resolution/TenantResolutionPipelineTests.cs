using MicroKit.Tenancy;
using MicroKit.Result;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;
using static MicroKit.Result.Result;

namespace MicroKit.Tenancy.UnitTests.Resolution;

public sealed class TenantResolutionPipelineTests
{
    private static TenantResolutionPipeline BuildPipeline(
        ITenantStore store,
        params ITenantResolutionStrategy[] strategies)
        => new(strategies, store, NullLogger<TenantResolutionPipeline>.Instance);

    private static Tenancy.TenantId NewId() => Tenancy.TenantId.NewId();

    private static ITenantResolutionStrategy FailingStrategy(int order)
    {
        var s = Substitute.For<ITenantResolutionStrategy>();
        s.Order.Returns(order);
        s.TryResolveAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(
                Failure<Tenancy.TenantId>(MultitenancyErrors.ResolutionFailed)));
        return s;
    }

    private static ITenantResolutionStrategy SucceedingStrategy(int order, Tenancy.TenantId id)
    {
        var s = Substitute.For<ITenantResolutionStrategy>();
        s.Order.Returns(order);
        s.TryResolveAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(id)));
        return s;
    }

    [Fact]
    public async Task ResolveAsync_WhenNoStrategiesRegistered_ReturnsResolutionFailed()
    {
        var store = Substitute.For<ITenantStore>();
        var pipeline = BuildPipeline(store);

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.ResolutionFailed);
    }

    [Fact]
    public async Task ResolveAsync_WhenFirstStrategySucceeds_ShortCircuits_SecondNotCalled()
    {
        var tenantId = NewId();
        var tenantInfo = Substitute.For<ITenantInfo>();

        var strategy1 = SucceedingStrategy(order: 1, tenantId);
        var strategy2 = Substitute.For<ITenantResolutionStrategy>();
        strategy2.Order.Returns(2);

        var store = Substitute.For<ITenantStore>();
        store.FindAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success<ITenantInfo>(tenantInfo)));

        var pipeline = BuildPipeline(store, strategy1, strategy2);

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        await strategy2.DidNotReceive().TryResolveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenFirstStrategyFails_TriesSecondStrategy()
    {
        var tenantId = NewId();
        var tenantInfo = Substitute.For<ITenantInfo>();

        var strategy1 = FailingStrategy(order: 1);
        var strategy2 = SucceedingStrategy(order: 2, tenantId);

        var store = Substitute.For<ITenantStore>();
        store.FindAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success<ITenantInfo>(tenantInfo)));

        var pipeline = BuildPipeline(store, strategy1, strategy2);

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        await strategy1.Received(1).TryResolveAsync(Arg.Any<CancellationToken>());
        await strategy2.Received(1).TryResolveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenAllStrategiesFail_ReturnsResolutionFailed()
    {
        var store = Substitute.For<ITenantStore>();
        var pipeline = BuildPipeline(store, FailingStrategy(1), FailingStrategy(2));

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.ResolutionFailed);
    }

    [Fact]
    public async Task ResolveAsync_ExecutesStrategiesInAscendingOrder()
    {
        var callOrder = new List<int>();

        var strategy20 = Substitute.For<ITenantResolutionStrategy>();
        strategy20.Order.Returns(20);
        strategy20.TryResolveAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add(20);
                return ValueTask.FromResult(Failure<Tenancy.TenantId>(MultitenancyErrors.ResolutionFailed));
            });

        var strategy10 = Substitute.For<ITenantResolutionStrategy>();
        strategy10.Order.Returns(10);
        strategy10.TryResolveAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add(10);
                return ValueTask.FromResult(Failure<Tenancy.TenantId>(MultitenancyErrors.ResolutionFailed));
            });

        var store = Substitute.For<ITenantStore>();
        // Strategies passed in WRONG order — pipeline must sort by Order ascending
        var pipeline = BuildPipeline(store, strategy20, strategy10);

        await pipeline.ResolveAsync();

        callOrder.Count.ShouldBe(2);
        callOrder[0].ShouldBe(10);
        callOrder[1].ShouldBe(20);
    }

    [Fact]
    public async Task ResolveAsync_WhenStrategyThrows_ContinuesToNextStrategy()
    {
        var tenantId = NewId();
        var tenantInfo = Substitute.For<ITenantInfo>();

        var throwingStrategy = Substitute.For<ITenantResolutionStrategy>();
        throwingStrategy.Order.Returns(1);
        throwingStrategy.TryResolveAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Strategy failed unexpectedly."));

        var successStrategy = SucceedingStrategy(order: 2, tenantId);

        var store = Substitute.For<ITenantStore>();
        store.FindAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success<ITenantInfo>(tenantInfo)));

        var pipeline = BuildPipeline(store, throwingStrategy, successStrategy);

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(tenantInfo);
    }

    [Fact]
    public async Task ResolveAsync_WhenStrategyResolves_CallsStoreWithResolvedTenantId()
    {
        var tenantId = NewId();
        var tenantInfo = Substitute.For<ITenantInfo>();
        var strategy = SucceedingStrategy(order: 1, tenantId);

        var store = Substitute.For<ITenantStore>();
        store.FindAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success<ITenantInfo>(tenantInfo)));

        var pipeline = BuildPipeline(store, strategy);

        await pipeline.ResolveAsync();

        await store.Received(1).FindAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenStoreReturnsNotFound_ReturnsStoreFailure()
    {
        var tenantId = NewId();
        var strategy = SucceedingStrategy(order: 1, tenantId);

        var store = Substitute.For<ITenantStore>();
        store.FindAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Failure<ITenantInfo>(MultitenancyErrors.TenantNotFound)));

        var pipeline = BuildPipeline(store, strategy);

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task ResolveAsync_WhenAllStrategiesThrow_ReturnsFailure()
    {
        var throwing = Substitute.For<ITenantResolutionStrategy>();
        throwing.Order.Returns(1);
        throwing.TryResolveAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));
        var store = Substitute.For<ITenantStore>();
        var pipeline = BuildPipeline(store, throwing);

        var result = await pipeline.ResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.ResolutionFailed);
    }
}
