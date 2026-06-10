namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakeCurrentUserBuilderTests
{
    [Fact]
    public void Create_ProducesAuthenticatedUserWithNonEmptyUserId()
    {
        var user = FakeCurrentUserBuilder.Create().Build();

        user.IsAuthenticated.ShouldBeTrue();
        user.UserId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Build_ReturnsICurrentUser()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        user.ShouldBeAssignableTo<ICurrentUser>();
    }

    [Fact]
    public void WithUserId_SetsUserId()
    {
        var expected = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithUserId(expected).Build();
        user.UserId.ShouldBe(expected);
    }

    [Fact]
    public void WithTenantId_SetsTenantId()
    {
        var expected = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithTenantId(expected).Build();
        user.TenantId.ShouldBe(expected);
    }

    [Fact]
    public void WithEmail_SetsEmail()
    {
        const string expected = "user@example.com";
        var user = FakeCurrentUserBuilder.Create().WithEmail(expected).Build();
        user.Email.ShouldBe(expected);
    }

    [Fact]
    public void WithRole_AddsRole()
    {
        var role = Role.Of("admin");
        var user = FakeCurrentUserBuilder.Create().WithRole(role).Build();
        user.Roles.ShouldContain(role);
    }

    [Fact]
    public void WithRole_CalledMultipleTimes_AccumulatesAllRoles()
    {
        var admin = Role.Of("admin");
        var auditor = Role.Of("auditor");
        var viewer = Role.Of("viewer");

        var user = FakeCurrentUserBuilder.Create()
            .WithRole(admin)
            .WithRole(auditor)
            .WithRole(viewer)
            .Build();

        user.Roles.Count.ShouldBe(3);
        user.Roles.ShouldContain(admin);
        user.Roles.ShouldContain(auditor);
        user.Roles.ShouldContain(viewer);
    }

    [Fact]
    public void WithClaim_AddsClaim()
    {
        var user = FakeCurrentUserBuilder.Create()
            .WithClaim("department", "engineering")
            .Build();

        user.Claims.ContainsKey("department").ShouldBeTrue();
        user.Claims["department"].ShouldBe("engineering");
    }

    [Fact]
    public void AsUnauthenticated_SetsIsAuthenticatedFalse()
    {
        var user = FakeCurrentUserBuilder.Create().AsUnauthenticated().Build();
        user.IsAuthenticated.ShouldBeFalse();
    }
}
