using MicroKit.Multitenancy;
using MicroKit.Multitenancy.AspNetCore;
using MicroKit.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using static MicroKit.Result.Result;

namespace MicroKit.Multitenancy.UnitTests.AspNetCore.Strategies;

public sealed class HeaderTenantResolutionStrategyTests
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
    public void Order_Is10()
    {
        var strategy = new HeaderTenantResolutionStrategy(
            Substitute.For<IHttpContextAccessor>(), DefaultOptions());
        strategy.Order.ShouldBe(10);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHttpContextIsNull_ReturnsTenantNotFound()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var strategy = new HeaderTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHeaderMissing_ReturnsTenantNotFound()
    {
        var (accessor, _) = SetupContext();
        var strategy = new HeaderTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHeaderIsNotAGuid_ReturnsInvalidTenantId()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.Headers.Append("X-Tenant-Id", "not-a-guid");
        var strategy = new HeaderTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.InvalidTenantId);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHeaderIsValidGuid_ReturnsSuccess()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.Request.Headers.Append("X-Tenant-Id", guid.ToString());
        var strategy = new HeaderTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }

    [Fact]
    public async Task TryResolveAsync_WhenCustomHeaderName_ReadsFromConfiguredHeader()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.Request.Headers.Append("X-Custom-Tenant", guid.ToString());
        var opts = Options.Create(new AspNetCoreMultitenancyOptions { HeaderName = "X-Custom-Tenant" });
        var strategy = new HeaderTenantResolutionStrategy(accessor, opts);

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }
}
