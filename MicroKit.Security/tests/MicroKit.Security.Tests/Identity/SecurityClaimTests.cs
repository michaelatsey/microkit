using MicroKit.Security.Abstractions.Identity;
using Xunit;

namespace MicroKit.Security.Tests.Identity;

public sealed class SecurityClaimTests
{
    [Fact]
    public void Empty_IsEmpty_ReturnsTrue()
    {
        Assert.True(SecurityClaim.Empty.IsEmpty);
    }

    [Fact]
    public void NonEmpty_IsEmpty_ReturnsFalse()
    {
        var claim = new SecurityClaim("role", "admin");
        Assert.False(claim.IsEmpty);
    }

    [Fact]
    public void IsType_WithMatchingType_ReturnsTrue()
    {
        var claim = new SecurityClaim("role", "admin");
        Assert.True(claim.IsType("role"));
    }

    [Fact]
    public void IsType_WithDifferentType_ReturnsFalse()
    {
        var claim = new SecurityClaim("role", "admin");
        Assert.False(claim.IsType("email"));
    }

    [Fact]
    public void Matches_WithMatchingTypeAndValue_ReturnsTrue()
    {
        var claim = new SecurityClaim("role", "admin");
        Assert.True(claim.Matches("role", "admin"));
    }

    [Fact]
    public void Matches_WithWrongValue_ReturnsFalse()
    {
        var claim = new SecurityClaim("role", "admin");
        Assert.False(claim.Matches("role", "user"));
    }

    [Fact]
    public void Matches_WithWrongType_ReturnsFalse()
    {
        var claim = new SecurityClaim("role", "admin");
        Assert.False(claim.Matches("scope", "admin"));
    }

    [Fact]
    public void Equality_SameTypeAndValue_AreEqual()
    {
        var a = new SecurityClaim("sub", "123");
        var b = new SecurityClaim("sub", "123");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        var a = new SecurityClaim("sub", "123");
        var b = new SecurityClaim("sub", "456");
        Assert.NotEqual(a, b);
    }
}
