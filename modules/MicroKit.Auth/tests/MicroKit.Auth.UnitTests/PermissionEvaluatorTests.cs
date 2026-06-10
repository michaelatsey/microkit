namespace MicroKit.Auth.UnitTests;

public sealed class PermissionEvaluatorTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission CreatePerm = Permission.Of("docs", "create");

    private readonly FakeCurrentUserAccessor _accessor = new();
    private readonly IPermissionStore _store = Substitute.For<IPermissionStore>();
    private readonly IRolePermissionMap _roleMap = Substitute.For<IRolePermissionMap>();
    private readonly PermissionEvaluator _sut;

    public PermissionEvaluatorTests()
    {
        _roleMap.GetPermissionsForRole(Arg.Any<Role>()).Returns(Array.Empty<Permission>());
        _sut = new PermissionEvaluator(_accessor, _store, _roleMap);
    }

    // ── System-level (IPermissionChecker) ─────────────────────────────────

    [Fact]
    public async Task HasPermissionAsync_WhenUserNotAuthenticated_ReturnsUnauthenticatedError()
    {
        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthenticatedError>();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenUserIsSuperAdmin_ReturnsTrueWithoutStoreLookup()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().WithRole(Role.Of("superadmin")).Build());

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().GetPermissionsAsync(default, default);
    }

    [Fact]
    public async Task HasPermissionAsync_WhenUserHasDirectPermission_ReturnsTrue()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [ReadPerm];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenPermissionNotGranted_ReturnsFalse()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenResourceWildcardGranted_ReturnsTrue()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [Permission.Of("docs", "*")];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(CreatePerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenActionWildcardGranted_ReturnsTrue()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [Permission.Of("*", "read")];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenFullWildcardGranted_ReturnsTrue()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [Permission.Of("*", "*")];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(CreatePerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenStoreReturnsFailure_PropagatesFailure()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _accessor.Set(user);
        var error = new UnauthenticatedError();
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(
                Failure<IReadOnlyList<Permission>>(error)));

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenRoleGrantsPermission_ReturnsTrue()
    {
        var role = Role.Of("editor");
        var user = FakeCurrentUserBuilder.Create().WithRole(role).Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> storePerms = [];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(storePerms)));
        IReadOnlyList<Permission> rolePerms = [ReadPerm];
        _roleMap.GetPermissionsForRole(role).Returns(rolePerms);

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenRoleMapReturnsEmpty_DoesNotGrantPermission()
    {
        var role = Role.Of("viewer");
        var user = FakeCurrentUserBuilder.Create().WithRole(role).Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> storePerms = [];
        _store.GetPermissionsAsync(user.UserId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(storePerms)));

        var result = await _sut.HasPermissionAsync(ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }

    // ── Tenant-scoped (ITenantPermissionChecker) ──────────────────────────

    [Fact]
    public async Task HasPermissionAsync_TenantScoped_WhenUserNotAuthenticated_ReturnsUnauthenticatedError()
    {
        var result = await _sut.HasPermissionAsync(Guid.NewGuid(), ReadPerm);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnauthenticatedError>();
    }

    [Fact]
    public async Task HasPermissionAsync_TenantScoped_WhenUserIsSuperAdmin_ReturnsTrueWithoutStoreLookup()
    {
        _accessor.Set(FakeCurrentUserBuilder.Create().WithRole(Role.Of("superadmin")).Build());

        var result = await _sut.HasPermissionAsync(Guid.NewGuid(), ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs()
            .GetPermissionsAsync(default, default, default);
    }

    [Fact]
    public async Task HasPermissionAsync_TenantScoped_WhenUserHasPermissionInTenant_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithTenantId(tenantId).Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [ReadPerm];
        _store.GetPermissionsAsync(user.UserId, tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(tenantId, ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_TenantScoped_WhenPermissionNotGranted_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var user = FakeCurrentUserBuilder.Create().WithTenantId(tenantId).Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> perms = [];
        _store.GetPermissionsAsync(user.UserId, tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(perms)));

        var result = await _sut.HasPermissionAsync(tenantId, ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_TenantOverload_WhenRoleGrantsPermission_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.Of("editor");
        var user = FakeCurrentUserBuilder.Create().WithTenantId(tenantId).WithRole(role).Build();
        _accessor.Set(user);
        IReadOnlyList<Permission> storePerms = [];
        _store.GetPermissionsAsync(user.UserId, tenantId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Success(storePerms)));
        IReadOnlyList<Permission> rolePerms = [ReadPerm];
        _roleMap.GetPermissionsForRole(role).Returns(rolePerms);

        var result = await _sut.HasPermissionAsync(tenantId, ReadPerm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }
}
