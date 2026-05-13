using MicroKit.Security.Abstractions.Constants;
using MicroKit.Security.Abstractions.Extensions;
using MicroKit.Security.Abstractions.Identity;
using Xunit;

namespace MicroKit.Security.Tests.Identity;

public sealed class SecurityPrincipalExtensionsTests
{
    private static SecurityPrincipal CreatePrincipal(params SecurityClaim[] claims)
        => new("user-1", "Alice", "tenant-a", claims);

    [Fact]
    public void GetRoles_ReturnsRoleAndRolesClaims()
    {
        var principal = CreatePrincipal(
            new SecurityClaim(ClaimTypes.Role, "admin"),
            new SecurityClaim(ClaimTypes.Roles, "editor"),
            new SecurityClaim(ClaimTypes.Email, "alice@example.com"));

        var roles = principal.GetRoles().ToList();

        Assert.Equal(2, roles.Count);
        Assert.Contains("admin", roles);
        Assert.Contains("editor", roles);
    }

    [Fact]
    public void HasRole_WhenRoleClaimPresent_ReturnsTrue()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Role, "admin"));
        Assert.True(principal.HasRole("admin"));
    }

    [Fact]
    public void HasRole_WhenRolesClaimPresent_ReturnsTrue()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Roles, "admin"));
        Assert.True(principal.HasRole("admin"));
    }

    [Fact]
    public void HasRole_WhenRoleAbsent_ReturnsFalse()
    {
        var principal = CreatePrincipal();
        Assert.False(principal.HasRole("admin"));
    }

    [Fact]
    public void HasPermission_WhenPermissionsClaimPresent_ReturnsTrue()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Permissions, "orders:read"));
        Assert.True(principal.HasPermission("orders:read"));
    }

    [Fact]
    public void HasPermission_WhenPermissionAbsent_ReturnsFalse()
    {
        var principal = CreatePrincipal();
        Assert.False(principal.HasPermission("orders:write"));
    }

    [Fact]
    public void HasAnyRole_WhenAtLeastOneRolePresent_ReturnsTrue()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Role, "editor"));
        Assert.True(principal.HasAnyRole("admin", "editor", "viewer"));
    }

    [Fact]
    public void HasAnyRole_WhenNoRoleMatches_ReturnsFalse()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Role, "viewer"));
        Assert.False(principal.HasAnyRole("admin", "editor"));
    }

    [Fact]
    public void HasAllRoles_WhenAllRolesPresent_ReturnsTrue()
    {
        var principal = CreatePrincipal(
            new SecurityClaim(ClaimTypes.Role, "admin"),
            new SecurityClaim(ClaimTypes.Role, "editor"));
        Assert.True(principal.HasAllRoles("admin", "editor"));
    }

    [Fact]
    public void HasAllRoles_WhenOneRoleMissing_ReturnsFalse()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Role, "admin"));
        Assert.False(principal.HasAllRoles("admin", "editor"));
    }

    [Fact]
    public void GetEmail_WhenEmailClaimPresent_ReturnsEmail()
    {
        var principal = CreatePrincipal(new SecurityClaim(ClaimTypes.Email, "alice@example.com"));
        Assert.Equal("alice@example.com", principal.GetEmail());
    }

    [Fact]
    public void GetEmail_WhenEmailClaimAbsent_ReturnsNull()
    {
        var principal = CreatePrincipal();
        Assert.Null(principal.GetEmail());
    }
}
