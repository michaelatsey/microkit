using MicroKit.Tenancy;
using MicroKit.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.UnitTests.AspNetCore.Strategies;

public sealed class RouteDataTenantResolutionStrategyTests
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
    public void Order_Is20()
    {
        var strategy = new RouteDataTenantResolutionStrategy(
            Substitute.For<IHttpContextAccessor>(), DefaultOptions());
        strategy.Order.ShouldBe(20);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHttpContextIsNull_ReturnsTenantNotFound()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var strategy = new RouteDataTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenRouteValueMissing_ReturnsTenantNotFound()
    {
        var (accessor, _) = SetupContext();
        var strategy = new RouteDataTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenRouteValueIsNotAGuid_ReturnsInvalidTenantId()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.RouteValues["tenantId"] = "not-a-guid";
        var strategy = new RouteDataTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.InvalidTenantId);
    }

    [Fact]
    public async Task TryResolveAsync_WhenRouteValueIsValidGuid_ReturnsSuccess()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.Request.RouteValues["tenantId"] = guid.ToString();
        var strategy = new RouteDataTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }

    [Fact]
    public async Task TryResolveAsync_WhenCustomParameterName_ReadsFromConfiguredKey()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.Request.RouteValues["tid"] = guid.ToString();
        var opts = Options.Create(new AspNetCoreMultitenancyOptions { RouteParameterName = "tid" });
        var strategy = new RouteDataTenantResolutionStrategy(accessor, opts);

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }
}
