namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakePermissionStoreTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission WritePerm = Permission.Of("docs", "write");
    private static readonly Permission DeletePerm = Permission.Of("docs", "delete");

    // ─── System-level tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPermissionsAsync_WhenUserHasSystemGrant_ReturnsGrantedPermissions()
    {
        var userId = Guid.NewGuid();
        var store = new FakePermissionStore().Grant(userId, ReadPerm);

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(ReadPerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenUserHasNoGrant_ReturnsEmptyList()
    {
        var store = new FakePermissionStore();

        var result = await store.GetPermissionsAsync(Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenGrantCalledTwice_AccumulatesPermissions()
    {
        var userId = Guid.NewGuid();
        var store = new FakePermissionStore()
            .Grant(userId, ReadPerm)
            .Grant(userId, WritePerm);

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(ReadPerm);
        result.Value.ShouldContain(WritePerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenSamePermissionGrantedTwice_DoesNotDuplicate()
    {
        var userId = Guid.NewGuid();
        var store = new FakePermissionStore()
            .Grant(userId, ReadPerm)
            .Grant(userId, ReadPerm);

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenClearCalled_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        var store = new FakePermissionStore()
            .Grant(userId, ReadPerm)
            .Clear();

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_TwoUsers_ReceiveDifferentPermissions()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var store = new FakePermissionStore()
            .Grant(userA, ReadPerm)
            .Grant(userB, WritePerm);

        var resultA = await store.GetPermissionsAsync(userA);
        var resultB = await store.GetPermissionsAsync(userB);

        resultA.IsSuccess.ShouldBeTrue();
        resultA.Value.ShouldContain(ReadPerm);
        resultA.Value.ShouldNotContain(WritePerm);

        resultB.IsSuccess.ShouldBeTrue();
        resultB.Value.ShouldContain(WritePerm);
        resultB.Value.ShouldNotContain(ReadPerm);
    }

    // ─── Tenant-scoped tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_WhenUserHasTenantGrant_ReturnsGrantedPermissions()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = new FakePermissionStore().GrantInTenant(userId, tenantId, WritePerm);

        var result = await store.GetPermissionsAsync(userId, tenantId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(WritePerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_WhenUserHasNoTenantGrant_ReturnsEmptyList()
    {
        var store = new FakePermissionStore();

        var result = await store.GetPermissionsAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_DoesNotReturnSystemGrantsForSameUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = new FakePermissionStore()
            .Grant(userId, ReadPerm)
            .GrantInTenant(userId, tenantId, WritePerm);

        var tenantResult = await store.GetPermissionsAsync(userId, tenantId);

        tenantResult.IsSuccess.ShouldBeTrue();
        tenantResult.Value.ShouldContain(WritePerm);
        tenantResult.Value.ShouldNotContain(ReadPerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_SystemLevel_DoesNotReturnTenantGrantsForSameUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = new FakePermissionStore()
            .Grant(userId, ReadPerm)
            .GrantInTenant(userId, tenantId, WritePerm);

        var systemResult = await store.GetPermissionsAsync(userId);

        systemResult.IsSuccess.ShouldBeTrue();
        systemResult.Value.ShouldContain(ReadPerm);
        systemResult.Value.ShouldNotContain(WritePerm);
    }
}
