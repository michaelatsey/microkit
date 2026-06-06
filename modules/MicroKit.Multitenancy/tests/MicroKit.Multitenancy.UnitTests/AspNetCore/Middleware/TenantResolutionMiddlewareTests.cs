using MicroKit.Multitenancy;
using MicroKit.Multitenancy.AspNetCore;
using MicroKit.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using static MicroKit.Result.Result;

namespace MicroKit.Multitenancy.UnitTests.AspNetCore.Middleware;

public sealed class TenantResolutionMiddlewareTests
{
    // ── Unit tests (substituted resolver + accessor) ────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenResolverSucceeds_SetsTenantOnAccessor()
    {
        var tenantInfo = Substitute.For<ITenantInfo>();
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success<ITenantInfo>(tenantInfo)));

        var accessor = Substitute.For<ITenantContextAccessor>();
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(
            new DefaultHttpContext(), resolver, accessor,
            NullLogger<TenantResolutionMiddleware>.Instance);

        accessor.Received(1).SetTenant(tenantInfo);
        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenResolverFails_DoesNotSetTenant_AndCallsNext()
    {
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Failure<ITenantInfo>(MultitenancyErrors.ResolutionFailed)));

        var accessor = Substitute.For<ITenantContextAccessor>();
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(
            new DefaultHttpContext(), resolver, accessor,
            NullLogger<TenantResolutionMiddleware>.Instance);

        accessor.DidNotReceive().SetTenant(Arg.Any<ITenantInfo>());
        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext_EvenOnResolutionFailure()
    {
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Failure<ITenantInfo>(MultitenancyErrors.TenantNotFound)));

        var nextCallCount = 0;
        var middleware = new TenantResolutionMiddleware(_ => { nextCallCount++; return Task.CompletedTask; });

        await middleware.InvokeAsync(
            new DefaultHttpContext(),
            resolver,
            Substitute.For<ITenantContextAccessor>(),
            NullLogger<TenantResolutionMiddleware>.Instance);

        nextCallCount.ShouldBe(1);
    }

    // ── Full-chain integration tests (real objects, no substitutes for core) ─

    [Fact]
    public async Task FullChain_WhenHeaderPresent_SetsTenantContextOnAccessor()
    {
        var tenantId = TenantId.NewId();
        var tenantInfo = Substitute.For<ITenantInfo>();
        tenantInfo.Id.Returns(tenantId);

        var store = new InMemoryTenantStore([tenantInfo]);
        var accessor = new AsyncLocalTenantContextAccessor();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append("X-Tenant-Id", tenantId.Value.ToString());

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var options = Options.Create(new AspNetCoreMultitenancyOptions());
        var headerStrategy = new HeaderTenantResolutionStrategy(httpContextAccessor, options);
        var pipeline = new TenantResolutionPipeline([headerStrategy], store);

        // AsyncLocal writes don't propagate back to the caller's execution context.
        // Capture CurrentTenant from inside the next delegate, which runs in the same
        // async chain where SetTenant was called.
        ITenantInfo? capturedTenant = null;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            capturedTenant = accessor.CurrentTenant;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(
            httpContext, pipeline, accessor,
            NullLogger<TenantResolutionMiddleware>.Instance);

        capturedTenant.ShouldBe(tenantInfo);
    }

    [Fact]
    public async Task FullChain_WhenNoStrategyResolves_LeavesContextNullAndContinues()
    {
        var store = new InMemoryTenantStore();
        var accessor = new AsyncLocalTenantContextAccessor();

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var options = Options.Create(new AspNetCoreMultitenancyOptions());
        var headerStrategy = new HeaderTenantResolutionStrategy(httpContextAccessor, options);
        var pipeline = new TenantResolutionPipeline([headerStrategy], store);

        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(
            new DefaultHttpContext(), pipeline, accessor,
            NullLogger<TenantResolutionMiddleware>.Instance);

        accessor.CurrentTenant.ShouldBeNull();
        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task FullChain_WhenTenantIdNotInStore_LeavesContextNullAndContinues()
    {
        var store = new InMemoryTenantStore();
        var accessor = new AsyncLocalTenantContextAccessor();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append("X-Tenant-Id", Guid.NewGuid().ToString());

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var options = Options.Create(new AspNetCoreMultitenancyOptions());
        var headerStrategy = new HeaderTenantResolutionStrategy(httpContextAccessor, options);
        var pipeline = new TenantResolutionPipeline([headerStrategy], store);

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(
            httpContext, pipeline, accessor,
            NullLogger<TenantResolutionMiddleware>.Instance);

        accessor.CurrentTenant.ShouldBeNull();
    }

    [Fact]
    public async Task FullChain_PipelineShortCircuitsOnFirstSuccessfulStrategy()
    {
        var tenantId = TenantId.NewId();
        var tenantInfo = Substitute.For<ITenantInfo>();
        tenantInfo.Id.Returns(tenantId);

        var store = new InMemoryTenantStore([tenantInfo]);
        var accessor = new AsyncLocalTenantContextAccessor();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append("X-Tenant-Id", tenantId.Value.ToString());
        httpContext.Request.RouteValues["tenantId"] = Guid.NewGuid().ToString();

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var options = Options.Create(new AspNetCoreMultitenancyOptions());
        var headerStrategy = new HeaderTenantResolutionStrategy(httpContextAccessor, options);
        var routeStrategy = Substitute.For<ITenantResolutionStrategy>();
        routeStrategy.Order.Returns(20);

        var pipeline = new TenantResolutionPipeline([headerStrategy, routeStrategy], store);

        // Read CurrentTenant inside next — same async chain where SetTenant was called
        ITenantInfo? capturedTenant = null;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            capturedTenant = accessor.CurrentTenant;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(
            httpContext, pipeline, accessor,
            NullLogger<TenantResolutionMiddleware>.Instance);

        capturedTenant.ShouldBe(tenantInfo);
        // Header (Order 10) resolved first — route strategy never called
        await routeStrategy.DidNotReceive().TryResolveAsync(Arg.Any<CancellationToken>());
    }
}
