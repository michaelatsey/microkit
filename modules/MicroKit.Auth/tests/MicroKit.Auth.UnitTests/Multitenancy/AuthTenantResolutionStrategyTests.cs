using MicroKit.Auth.Multitenancy;
using MicroKit.Tenancy;

namespace MicroKit.Auth.UnitTests.Multitenancy;

public sealed class AuthTenantResolutionStrategyTests
{
    private readonly FakeCurrentUserAccessor _accessor = new();

    [Fact]
    public void Order_IsForty()
    {
        var sut = new AuthTenantResolutionStrategy(_accessor);

        sut.Order.ShouldBe(40);
    }

    [Fact]
    public async Task TryResolveAsync_WhenUserNull_ReturnsFailure()
    {
        var sut = new AuthTenantResolutionStrategy(_accessor);

        var result = await sut.TryResolveAsync();

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task TryResolveAsync_WhenUserNotAuthenticated_ReturnsFailure()
    {
        var user = FakeCurrentUserBuilder.Create()
            .AsUnauthenticated()
            .Build();
        _accessor.Set(user);
        var sut = new AuthTenantResolutionStrategy(_accessor);

        var result = await sut.TryResolveAsync();

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task TryResolveAsync_WhenUserHasNoTenantId_ReturnsFailure()
    {
        var user = FakeCurrentUserBuilder.Create()
            .WithUserId(Guid.NewGuid())
            .Build();
        _accessor.Set(user);
        var sut = new AuthTenantResolutionStrategy(_accessor);

        var result = await sut.TryResolveAsync();

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task TryResolveAsync_WhenUserAuthenticatedWithTenantId_ReturnsTenantId()
    {
        var tenantGuid = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create()
            .WithUserId(Guid.NewGuid())
            .WithTenantId(tenantGuid)
            .Build();
        _accessor.Set(user);
        var sut = new AuthTenantResolutionStrategy(_accessor);

        var result = await sut.TryResolveAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(tenantGuid);
    }
}
