using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Core.Contexts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MicroKit.Security.Tests.Contexts;

public sealed class SecurityContextFactoryTests
{
    private static SecurityContextFactory CreateFactory()
        => new(TimeProvider.System, NullLogger<SecurityContextFactory>.Instance);

    private static SecurityPrincipal CreatePrincipal(string? tenantId = null)
        => new("user-1", "Alice", tenantId, []);

    [Fact]
    public void CreateContext_PopulatesSchemeAndPrincipal()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal();

        var ctx = factory.CreateContext(principal, AuthenticationScheme.Jwt);

        Assert.Equal(AuthenticationScheme.Jwt, ctx.Scheme);
        Assert.Same(principal, ctx.Principal);
        Assert.True(ctx.IsAuthenticated);
    }

    [Fact]
    public void CreateContext_UsesProvidedCorrelationId()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal();

        var ctx = factory.CreateContext(principal, AuthenticationScheme.Jwt, correlationId: "corr-123");

        Assert.Equal("corr-123", ctx.CorrelationId);
    }

    [Fact]
    public void CreateContext_GeneratesCorrelationIdWhenNull()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal();

        var ctx = factory.CreateContext(principal, AuthenticationScheme.Jwt);

        Assert.NotEmpty(ctx.CorrelationId);
    }

    [Fact]
    public void CreateContext_UsesPrincipalTenantId_WhenHeaderIsNull()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal(tenantId: "tenant-jwt");

        var ctx = factory.CreateContext(principal, AuthenticationScheme.Jwt, tenantId: null);

        Assert.Equal("tenant-jwt", ctx.TenantId);
    }

    [Fact]
    public void CreateContext_UsesPrincipalTenantId_WhenHeaderMatchesPrincipal()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal(tenantId: "tenant-jwt");

        var ctx = factory.CreateContext(principal, AuthenticationScheme.Jwt, tenantId: "tenant-jwt");

        Assert.Equal("tenant-jwt", ctx.TenantId);
    }

    [Fact]
    public void CreateContext_UsesHeaderTenant_WhenPrincipalHasNoTenant()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal(tenantId: null);

        var ctx = factory.CreateContext(principal, AuthenticationScheme.ApiKey, tenantId: "tenant-header");

        Assert.Equal("tenant-header", ctx.TenantId);
    }

    [Fact]
    public void CreateContext_ThrowsOnTenantMismatch()
    {
        var factory = CreateFactory();
        var principal = CreatePrincipal(tenantId: "tenant-jwt");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            factory.CreateContext(principal, AuthenticationScheme.Jwt, tenantId: "tenant-other"));

        Assert.Contains("Tenant mismatch", ex.Message);
    }
}
