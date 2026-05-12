using MicroKit.MultiTenancy.ResolutionStrategies;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Security.Claims;

namespace MicroKit.MultiTenancy.Tests;

public sealed class TenantResolutionStrategyTests
{
    // ── HeaderResolutionStrategy ──────────────────────────────────────────────

    [Fact]
    public async Task HeaderStrategy_HeaderPresent_ReturnsTenantId()
    {
        var ctx = BuildHttpContext(headers: new Dictionary<string, string>
        {
            ["X-Tenant-Id"] = "acme"
        });
        var accessor = BuildAccessor(ctx);
        var strategy = new HeaderResolutionStrategy("X-Tenant-Id", accessor);

        var result = await strategy.ResolveAsync();

        Assert.Equal("acme", result);
    }

    [Fact]
    public async Task HeaderStrategy_HeaderMissing_ReturnsNull()
    {
        var ctx = BuildHttpContext();
        var accessor = BuildAccessor(ctx);
        var strategy = new HeaderResolutionStrategy("X-Tenant-Id", accessor);

        var result = await strategy.ResolveAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task HeaderStrategy_NoHttpContext_ReturnsNull()
    {
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var strategy = new HeaderResolutionStrategy("X-Tenant-Id", accessorMock.Object);

        var result = await strategy.ResolveAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task HeaderStrategy_GetTenantIdentifier_ReturnsHeaderValue()
    {
        var ctx = BuildHttpContext(headers: new Dictionary<string, string>
        {
            ["X-Tenant-Id"] = "fabrikam"
        });
        var accessor = BuildAccessor(ctx);
        var strategy = new HeaderResolutionStrategy("X-Tenant-Id", accessor);

        var result = await strategy.GetTenantIdentifierAsync(ctx);

        Assert.Equal("fabrikam", result);
    }

    // ── JwtClaimResolutionStrategy ────────────────────────────────────────────

    [Fact]
    public async Task JwtStrategy_ClaimPresent_ReturnsTenantId()
    {
        var ctx = BuildHttpContextWithClaims(new Claim("tenant_id", "contoso"));
        var accessor = BuildAccessor(ctx);
        var strategy = new JwtClaimResolutionStrategy("tenant_id", accessor);

        var result = await strategy.ResolveAsync();

        Assert.Equal("contoso", result);
    }

    [Fact]
    public async Task JwtStrategy_ClaimMissing_ReturnsNull()
    {
        var ctx = BuildHttpContext();
        var accessor = BuildAccessor(ctx);
        var strategy = new JwtClaimResolutionStrategy("tenant_id", accessor);

        var result = await strategy.ResolveAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task JwtStrategy_NoHttpContext_ReturnsNull()
    {
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var strategy = new JwtClaimResolutionStrategy("tenant_id", accessorMock.Object);

        var result = await strategy.ResolveAsync();

        Assert.Null(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DefaultHttpContext BuildHttpContext(
        Dictionary<string, string>? headers = null)
    {
        var ctx = new DefaultHttpContext();
        if (headers is not null)
        {
            foreach (var (k, v) in headers)
                ctx.Request.Headers[k] = v;
        }
        return ctx;
    }

    private static DefaultHttpContext BuildHttpContextWithClaims(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return ctx;
    }

    private static IHttpContextAccessor BuildAccessor(HttpContext ctx)
    {
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(a => a.HttpContext).Returns(ctx);
        return mock.Object;
    }
}
