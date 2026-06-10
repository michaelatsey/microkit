namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakeRoleStoreTests
{
    private static readonly Role AdminRole = Role.Of("admin");
    private static readonly Role AuditorRole = Role.Of("auditor");
    private static readonly Role ViewerRole = Role.Of("viewer");

    // ─── System-level tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetRolesAsync_WhenUserHasSystemGrant_ReturnsGrantedRoles()
    {
        var userId = Guid.NewGuid();
        var store = new FakeRoleStore().Grant(userId, AdminRole);

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(AdminRole);
    }

    [Fact]
    public async Task GetRolesAsync_WhenUserHasNoGrant_ReturnsEmptyList()
    {
        var store = new FakeRoleStore();

        var result = await store.GetRolesAsync(Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRolesAsync_WhenGrantCalledTwice_AccumulatesRoles()
    {
        var userId = Guid.NewGuid();
        var store = new FakeRoleStore()
            .Grant(userId, AdminRole)
            .Grant(userId, AuditorRole);

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(AdminRole);
        result.Value.ShouldContain(AuditorRole);
    }

    [Fact]
    public async Task GetRolesAsync_WhenSameRoleGrantedTwice_DoesNotDuplicate()
    {
        var userId = Guid.NewGuid();
        var store = new FakeRoleStore()
            .Grant(userId, AdminRole)
            .Grant(userId, AdminRole);

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetRolesAsync_WhenClearCalled_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        var store = new FakeRoleStore()
            .Grant(userId, AdminRole)
            .Clear();

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRolesAsync_TwoUsers_ReceiveDifferentRoles()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var store = new FakeRoleStore()
            .Grant(userA, AdminRole)
            .Grant(userB, ViewerRole);

        var resultA = await store.GetRolesAsync(userA);
        var resultB = await store.GetRolesAsync(userB);

        resultA.IsSuccess.ShouldBeTrue();
        resultA.Value.ShouldContain(AdminRole);
        resultA.Value.ShouldNotContain(ViewerRole);

        resultB.IsSuccess.ShouldBeTrue();
        resultB.Value.ShouldContain(ViewerRole);
        resultB.Value.ShouldNotContain(AdminRole);
    }

    // ─── Tenant-scoped tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetRolesAsync_TenantScoped_WhenUserHasTenantGrant_ReturnsGrantedRoles()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = new FakeRoleStore().GrantInTenant(userId, tenantId, AuditorRole);

        var result = await store.GetRolesAsync(userId, tenantId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(AuditorRole);
    }

    [Fact]
    public async Task GetRolesAsync_TenantScoped_WhenUserHasNoTenantGrant_ReturnsEmptyList()
    {
        var store = new FakeRoleStore();

        var result = await store.GetRolesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRolesAsync_TenantScoped_DoesNotReturnSystemGrantsForSameUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = new FakeRoleStore()
            .Grant(userId, AdminRole)
            .GrantInTenant(userId, tenantId, AuditorRole);

        var tenantResult = await store.GetRolesAsync(userId, tenantId);

        tenantResult.IsSuccess.ShouldBeTrue();
        tenantResult.Value.ShouldContain(AuditorRole);
        tenantResult.Value.ShouldNotContain(AdminRole);
    }

    [Fact]
    public async Task GetRolesAsync_SystemLevel_DoesNotReturnTenantGrantsForSameUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = new FakeRoleStore()
            .Grant(userId, AdminRole)
            .GrantInTenant(userId, tenantId, AuditorRole);

        var systemResult = await store.GetRolesAsync(userId);

        systemResult.IsSuccess.ShouldBeTrue();
        systemResult.Value.ShouldContain(AdminRole);
        systemResult.Value.ShouldNotContain(AuditorRole);
    }
}
