namespace MicroKit.Auth.UnitTests;

public sealed class SecurityContextTests
{
    private readonly FakeCurrentUserAccessor _accessor = new();
    private readonly SecurityContext _sut;

    public SecurityContextTests()
    {
        _sut = new SecurityContext(_accessor);
    }

    [Fact]
    public void CurrentUser_WhenNoUserSet_ReturnsAnonymous()
    {
        _sut.CurrentUser.ShouldBe(CurrentUser.Anonymous);
    }

    [Fact]
    public void IsAuthenticated_WhenNoUserSet_ReturnsFalse()
    {
        _sut.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void IsAuthenticated_WhenAuthenticatedUserSet_ReturnsTrue()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().Build());

        _sut.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void IsAuthenticated_WhenUnauthenticatedUserSet_ReturnsFalse()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().AsUnauthenticated().Build());

        _sut.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void HasTenant_WhenTenantIdPresent_ReturnsTrue()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().WithTenantId(Guid.NewGuid()).Build());

        _sut.HasTenant.ShouldBeTrue();
    }

    [Fact]
    public void HasTenant_WhenTenantIdNull_ReturnsFalse()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().Build());

        _sut.HasTenant.ShouldBeFalse();
    }

    [Fact]
    public void CurrentUser_AfterSet_ReturnsThatUser()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);

        _sut.CurrentUser.ShouldBe(user);
    }
}
