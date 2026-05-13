using MicroKit.Security.Abstractions.Constants;
using MicroKit.Security.Abstractions.Extensions;
using MicroKit.Security.Abstractions.Identity;
using Xunit;

namespace MicroKit.Security.Tests.Identity;

public sealed class SecurityPrincipalTests
{
    private static SecurityPrincipal CreatePrincipal(
        string? id = "user-1",
        string? displayName = "Alice",
        string? tenantId = "tenant-a",
        params SecurityClaim[] claims)
        => new(id, displayName, tenantId, claims);

    [Fact]
    public void IsAuthenticated_WithIdentifier_ReturnsTrue()
    {
        var p = CreatePrincipal();
        Assert.True(p.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_WithNullIdentifier_ReturnsFalse()
    {
        var p = CreatePrincipal(id: null);
        Assert.False(p.IsAuthenticated);
    }

    [Fact]
    public void HasClaim_WhenTypePresent_ReturnsTrue()
    {
        var p = CreatePrincipal(claims: new SecurityClaim(ClaimTypes.Role, "admin"));
        Assert.True(p.HasClaim(ClaimTypes.Role));
    }

    [Fact]
    public void HasClaim_TypeAndValue_WhenMatch_ReturnsTrue()
    {
        var p = CreatePrincipal(claims: new SecurityClaim(ClaimTypes.Email, "alice@example.com"));
        Assert.True(p.HasClaim(ClaimTypes.Email, "alice@example.com"));
    }

    [Fact]
    public void HasClaim_TypeAndValue_WhenValueMismatch_ReturnsFalse()
    {
        var p = CreatePrincipal(claims: new SecurityClaim(ClaimTypes.Email, "alice@example.com"));
        Assert.False(p.HasClaim(ClaimTypes.Email, "bob@example.com"));
    }

    [Fact]
    public void GetClaimValue_WhenClaimExists_ReturnsValue()
    {
        var p = CreatePrincipal(claims: new SecurityClaim(ClaimTypes.TenantId, "tenant-x"));
        Assert.Equal("tenant-x", p.GetClaimValue(ClaimTypes.TenantId));
    }

    [Fact]
    public void GetClaimValue_WhenClaimMissing_ReturnsNull()
    {
        var p = CreatePrincipal();
        Assert.Null(p.GetClaimValue(ClaimTypes.TenantId));
    }

    [Fact]
    public void WithClaims_AddsClaims_ReturnsNewInstance()
    {
        var p = CreatePrincipal();
        var updated = p.WithClaims(new SecurityClaim(ClaimTypes.Scope, "read"));

        Assert.NotSame(p, updated);
        Assert.Contains(updated.Claims, c => c.Type == ClaimTypes.Scope && c.Value == "read");
    }

    [Fact]
    public void WithTenant_ChangesOnlyTenantId()
    {
        var p = CreatePrincipal(tenantId: "old-tenant");
        var updated = p.WithTenant("new-tenant");

        Assert.Equal("new-tenant", updated.TenantId);
        Assert.Equal(p.Identifier, updated.Identifier);
    }

    [Fact]
    public void HasRole_UsingExtension_WhenRoleClaimPresent_ReturnsTrue()
    {
        var p = CreatePrincipal(claims: new SecurityClaim(ClaimTypes.Role, "admin"));
        Assert.True(p.HasRole("admin"));
    }

    [Fact]
    public void AnonymousPrincipal_IsNotAuthenticated()
    {
        var anon = AnonymousPrincipal.Instance;
        Assert.False(anon.IsAuthenticated);
        Assert.Empty(anon.Claims);
    }
}
