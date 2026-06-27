using MicroKit.Tenancy;
using MicroKit.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.UnitTests.AspNetCore.Strategies;

public sealed class SubdomainTenantResolutionStrategyTests
{
    private static IOptions<AspNetCoreMultitenancyOptions> DefaultOptions()
        => Options.Create(new AspNetCoreMultitenancyOptions());

    private static (IHttpContextAccessor, DefaultHttpContext) SetupContext()
    {
        var ctx = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return (accessor, ctx);
    }

    [Fact]
    public void Order_Is30()
    {
        var strategy = new SubdomainTenantResolutionStrategy(
            Substitute.For<IHttpContextAccessor>(), DefaultOptions());
        strategy.Order.ShouldBe(30);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHttpContextIsNull_ReturnsTenantNotFound()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var strategy = new SubdomainTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHostIsEmpty_ReturnsTenantNotFound()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.Host = new HostString("");
        var strategy = new SubdomainTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenSubdomainIsNotAGuid_ReturnsInvalidTenantId()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.Host = new HostString("acme.example.com");
        var strategy = new SubdomainTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.InvalidTenantId);
    }

    [Fact]
    public async Task TryResolveAsync_WhenSubdomainIsValidGuid_ReturnsSuccess()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.Request.Host = new HostString($"{guid}.example.com");
        var strategy = new SubdomainTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }

    [Fact]
    public async Task TryResolveAsync_WhenIndexBeyondSegments_ReturnsTenantNotFound()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.Host = new HostString("example.com");
        var opts = Options.Create(new AspNetCoreMultitenancyOptions { SubdomainSegmentIndex = 5 });
        var strategy = new SubdomainTenantResolutionStrategy(accessor, opts);

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }
}
