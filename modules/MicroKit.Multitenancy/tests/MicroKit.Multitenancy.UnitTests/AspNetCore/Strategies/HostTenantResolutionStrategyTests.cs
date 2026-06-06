using MicroKit.Multitenancy;
using MicroKit.Multitenancy.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Multitenancy.UnitTests.AspNetCore.Strategies;

public sealed class HostTenantResolutionStrategyTests
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
    public void Order_Is50()
    {
        var strategy = new HostTenantResolutionStrategy(
            Substitute.For<IHttpContextAccessor>(), DefaultOptions());
        strategy.Order.ShouldBe(50);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHttpContextIsNull_ReturnsTenantNotFound()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var strategy = new HostTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHostIsEmpty_ReturnsTenantNotFound()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.Host = new HostString("");
        var strategy = new HostTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHostNotInMappings_ReturnsTenantNotFound()
    {
        var (accessor, ctx) = SetupContext();
        ctx.Request.Host = new HostString("unknown.example.com");
        var strategy = new HostTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHostInMappings_ReturnsSuccess()
    {
        var (accessor, ctx) = SetupContext();
        var tenantId = TenantId.NewId();
        ctx.Request.Host = new HostString("tenant1.example.com");
        var opts = Options.Create(new AspNetCoreMultitenancyOptions
        {
            HostMappings = { ["tenant1.example.com"] = tenantId }
        });
        var strategy = new HostTenantResolutionStrategy(accessor, opts);

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(tenantId);
    }

    [Fact]
    public async Task TryResolveAsync_HostMappingIsCaseInsensitive()
    {
        var (accessor, ctx) = SetupContext();
        var tenantId = TenantId.NewId();
        ctx.Request.Host = new HostString("TENANT1.EXAMPLE.COM");
        var opts = Options.Create(new AspNetCoreMultitenancyOptions
        {
            HostMappings = { ["tenant1.example.com"] = tenantId }
        });
        var strategy = new HostTenantResolutionStrategy(accessor, opts);

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(tenantId);
    }
}
