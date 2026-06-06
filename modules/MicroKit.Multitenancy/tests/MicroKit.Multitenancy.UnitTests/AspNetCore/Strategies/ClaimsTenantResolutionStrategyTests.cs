using System.Security.Claims;
using MicroKit.Multitenancy;
using MicroKit.Multitenancy.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.Multitenancy.UnitTests.AspNetCore.Strategies;

public sealed class ClaimsTenantResolutionStrategyTests
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
    public void Order_Is40()
    {
        var strategy = new ClaimsTenantResolutionStrategy(
            Substitute.For<IHttpContextAccessor>(), DefaultOptions());
        strategy.Order.ShouldBe(40);
    }

    [Fact]
    public async Task TryResolveAsync_WhenHttpContextIsNull_ReturnsTenantNotFound()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var strategy = new ClaimsTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenClaimMissing_ReturnsTenantNotFound()
    {
        var (accessor, ctx) = SetupContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        var strategy = new ClaimsTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.TenantNotFound);
    }

    [Fact]
    public async Task TryResolveAsync_WhenClaimIsNotAGuid_ReturnsInvalidTenantId()
    {
        var (accessor, ctx) = SetupContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "not-a-guid")]));
        var strategy = new ClaimsTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(MultitenancyErrors.InvalidTenantId);
    }

    [Fact]
    public async Task TryResolveAsync_WhenClaimIsValidGuid_ReturnsSuccess()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", guid.ToString())]));
        var strategy = new ClaimsTenantResolutionStrategy(accessor, DefaultOptions());

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }

    [Fact]
    public async Task TryResolveAsync_WhenCustomClaimType_ReadsFromConfiguredClaim()
    {
        var (accessor, ctx) = SetupContext();
        var guid = Guid.NewGuid();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("x-custom-tenant", guid.ToString())]));
        var opts = Options.Create(new AspNetCoreMultitenancyOptions { ClaimType = "x-custom-tenant" });
        var strategy = new ClaimsTenantResolutionStrategy(accessor, opts);

        var result = await strategy.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(guid);
    }
}
