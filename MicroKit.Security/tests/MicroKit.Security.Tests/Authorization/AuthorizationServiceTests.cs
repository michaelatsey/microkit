using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Core.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MicroKit.Security.Tests.Authorization;

public sealed class AuthorizationServiceTests
{
    private static IOptions<SecurityOptions> DefaultOptions =>
        Options.Create(new SecurityOptions());

    private static AuthorizationService CreateService(SecurityOptions? options = null)
        => new(Options.Create(options ?? new SecurityOptions()), NullLogger<AuthorizationService>.Instance);

    private static SecurityPrincipal CreatePrincipal(params SecurityClaim[] claims)
        => new("user-1", "Alice", "tenant-a", claims);

    [Fact]
    public void IsAuthorized_WhenNotAuthenticated_ReturnsFalse()
    {
        var svc = CreateService();
        var anon = new SecurityPrincipal(null, null, null, []);
        Assert.False(svc.IsAuthorized(anon, "read"));
    }

    [Fact]
    public void IsAuthorized_WithNoPermissionsRequired_ReturnsTrue()
    {
        var svc = CreateService();
        var principal = CreatePrincipal();
        Assert.True(svc.IsAuthorized(principal));
    }

    [Fact]
    public void IsAuthorized_WhenRoleClaimMatches_ReturnsTrue()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(new SecurityClaim("role", "admin"));
        Assert.True(svc.IsAuthorized(principal, "admin"));
    }

    [Fact]
    public void IsAuthorized_WhenPermissionClaimMatches_ReturnsTrue()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(new SecurityClaim("permission", "orders:read"));
        Assert.True(svc.IsAuthorized(principal, "orders:read"));
    }

    [Fact]
    public void IsAuthorized_WhenScopeClaimMatches_ReturnsTrue()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(new SecurityClaim("scope", "api.read"));
        Assert.True(svc.IsAuthorized(principal, "api.read"));
    }

    [Fact]
    public void IsAuthorized_ORLogic_WhenOneMatchExists_ReturnsTrue()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(new SecurityClaim("role", "editor"));
        Assert.True(svc.IsAuthorized(principal, "admin", "editor", "viewer"));
    }

    [Fact]
    public void IsAuthorized_WhenNoneMatch_ReturnsFalse()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(new SecurityClaim("role", "viewer"));
        Assert.False(svc.IsAuthorized(principal, "admin", "editor"));
    }

    [Fact]
    public void HasAllPermissions_WhenAllMatch_ReturnsTrue()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(
            new SecurityClaim("permission", "orders:read"),
            new SecurityClaim("permission", "orders:write"));
        Assert.True(svc.HasAllPermissions(principal, "orders:read", "orders:write"));
    }

    [Fact]
    public void HasAllPermissions_WhenOneMissing_ReturnsFalse()
    {
        var svc = CreateService();
        var principal = CreatePrincipal(new SecurityClaim("permission", "orders:read"));
        Assert.False(svc.HasAllPermissions(principal, "orders:read", "orders:write"));
    }

    [Fact]
    public void HasAllPermissions_WhenNotAuthenticated_ReturnsFalse()
    {
        var svc = CreateService();
        var anon = new SecurityPrincipal(null, null, null, []);
        Assert.False(svc.HasAllPermissions(anon, "read"));
    }
}
