namespace MicroKit.Auth.UnitTests;

public sealed class RoleEvaluatorTests
{
    private readonly FakeCurrentUserAccessor _accessor = new();
    private readonly IRoleStore _store = Substitute.For<IRoleStore>();
    private readonly RoleEvaluator _sut;

    public RoleEvaluatorTests()
    {
        _sut = new RoleEvaluator(_accessor, _store);
    }

    // ── System-level (IRoleChecker) ────────────────────────────────────────

    [Fact]
    public async Task HasRoleAsync_WhenUnauthenticated_ReturnsUnauthenticatedError()
    {
        var result = await _sut.HasRoleAsync(SystemRoles.Admin);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthenticatedError>();
    }

    [Fact]
    public async Task HasRoleAsync_WhenUserHasJwtRole_ReturnsTrue()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().WithRole(SystemRoles.Admin).Build());

        var result = await _sut.HasRoleAsync(SystemRoles.Admin);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().GetRolesAsync(default, default);
    }

    [Fact]
    public async Task HasRoleAsync_WhenUserHasStoreRole_ReturnsTrue()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Role> roles = [SystemRoles.Manager];
        _store.GetRolesAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(roles)));

        var result = await _sut.HasRoleAsync(SystemRoles.Manager);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasRoleAsync_WhenRoleNotInJwtOrStore_ReturnsFalse()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Role> roles = [];
        _store.GetRolesAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(roles)));

        var result = await _sut.HasRoleAsync(SystemRoles.Admin);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task HasRoleAsync_WhenStoreReturnsFailure_PropagatesFailure()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        var error = new UnauthenticatedError();
        _store.GetRolesAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Failure<IReadOnlyList<Role>>(error)));

        var result = await _sut.HasRoleAsync(SystemRoles.Admin);

        result.IsFailure.ShouldBeTrue();
    }

    // ── Tenant-scoped (ITenantRoleChecker) ────────────────────────────────

    [Fact]
    public async Task HasRoleAsync_TenantOverload_WhenUnauthenticated_ReturnsUnauthenticatedError()
    {
        var result = await _sut.HasRoleAsync(Guid.NewGuid(), SystemRoles.Admin);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthenticatedError>();
    }

    [Fact]
    public async Task HasRoleAsync_TenantOverload_WhenUserHasJwtRole_ReturnsTrue()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().WithRole(SystemRoles.Admin).Build());

        var result = await _sut.HasRoleAsync(Guid.NewGuid(), SystemRoles.Admin);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().GetRolesAsync(default, default, default);
    }

    [Fact]
    public async Task HasRoleAsync_TenantOverload_WhenUserHasStoreRole_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithTenantId(tenantId).Build();
        _accessor.Set(user);
        IReadOnlyList<Role> roles = [SystemRoles.Operator];
        _store.GetRolesAsync(user.UserId, tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(roles)));

        var result = await _sut.HasRoleAsync(tenantId, SystemRoles.Operator);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }
}
