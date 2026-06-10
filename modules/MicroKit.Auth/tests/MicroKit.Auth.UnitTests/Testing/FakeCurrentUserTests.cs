namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakeCurrentUserTests
{
    [Fact]
    public void Default_UserId_IsNonEmpty()
    {
        var user = new FakeCurrentUser();
        user.UserId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Default_IsAuthenticated_IsTrue()
    {
        var user = new FakeCurrentUser();
        user.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void Default_Roles_IsEmpty()
    {
        var user = new FakeCurrentUser();
        user.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void Default_TenantId_IsNull()
    {
        var user = new FakeCurrentUser();
        user.TenantId.ShouldBeNull();
    }

    [Fact]
    public void Default_Email_IsNull()
    {
        var user = new FakeCurrentUser();
        user.Email.ShouldBeNull();
    }

    [Fact]
    public void Default_Claims_IsEmpty()
    {
        var user = new FakeCurrentUser();
        user.Claims.ShouldNotBeNull();
        user.Claims.ShouldBeEmpty();
    }

    [Fact]
    public void UserId_WhenSet_ReturnsNewValue()
    {
        var expected = Guid.NewGuid();
        var user = new FakeCurrentUser { UserId = expected };
        user.UserId.ShouldBe(expected);
    }

    [Fact]
    public void TenantId_WhenSet_ReturnsNewValue()
    {
        var expected = Guid.NewGuid();
        var user = new FakeCurrentUser { TenantId = expected };
        user.TenantId.ShouldBe(expected);
    }

    [Fact]
    public void Email_WhenSet_ReturnsNewValue()
    {
        const string expected = "test@example.com";
        var user = new FakeCurrentUser { Email = expected };
        user.Email.ShouldBe(expected);
    }

    [Fact]
    public void IsAuthenticated_WhenSetToFalse_ReturnsFalse()
    {
        var user = new FakeCurrentUser { IsAuthenticated = false };
        user.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void Roles_WhenSet_ReturnsAssignedRoles()
    {
        var roles = new[] { Role.Of("admin"), Role.Of("viewer") };
        var user = new FakeCurrentUser { Roles = roles };
        user.Roles.ShouldBe(roles);
    }
}
