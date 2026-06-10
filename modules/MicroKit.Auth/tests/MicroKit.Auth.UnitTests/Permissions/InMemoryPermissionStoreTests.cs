namespace MicroKit.Auth.UnitTests.Permissions;

public sealed class InMemoryPermissionStoreTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission CreatePerm = Permission.Of("docs", "create");
    private static readonly Permission UpdatePerm = Permission.Of("docs", "update");

    private static IPermissionStore CreateStore(Action<InMemoryPermissionStoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryPermissions(opts => configure?.Invoke(opts));
        return services.BuildServiceProvider().GetRequiredService<IPermissionStore>();
    }

    // ── System-level (Grant / GetPermissionsAsync(userId)) ────────────────

    [Fact]
    public async Task GetPermissionsAsync_WhenUserHasSystemGrant_ReturnsGrantedPermissions()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(opts => opts.Grant(userId, ReadPerm));

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(ReadPerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenUserHasNoSystemGrant_ReturnsEmptyList()
    {
        var store = CreateStore();

        var result = await store.GetPermissionsAsync(Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenGrantCalledTwice_AccumulatesPermissions()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.Grant(userId, ReadPerm);
            opts.Grant(userId, CreatePerm);
        });

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(ReadPerm);
        result.Value.ShouldContain(CreatePerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenGrantCalledWithMultiplePermissions_ReturnsAll()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(opts => opts.Grant(userId, ReadPerm, CreatePerm, UpdatePerm));

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetPermissionsAsync_WhenGrantCalledWithDuplicates_DoesNotDuplicate()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.Grant(userId, ReadPerm);
            opts.Grant(userId, ReadPerm);
        });

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
    }

    // ── Tenant-scoped (GrantInTenant / GetPermissionsAsync(userId, tenantId)) ──

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_WhenUserHasTenantGrant_ReturnsGrantedPermissions()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore(opts => opts.GrantInTenant(userId, tenantId, ReadPerm));

        var result = await store.GetPermissionsAsync(userId, tenantId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(ReadPerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_WhenUserHasNoTenantGrant_ReturnsEmptyList()
    {
        var store = CreateStore();

        var result = await store.GetPermissionsAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_WhenGrantInTenantCalledTwice_AccumulatesPermissions()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.GrantInTenant(userId, tenantId, ReadPerm);
            opts.GrantInTenant(userId, tenantId, CreatePerm);
        });

        var result = await store.GetPermissionsAsync(userId, tenantId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(ReadPerm);
        result.Value.ShouldContain(CreatePerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_WhenGrantedInDifferentTenants_ReturnsSeparately()
    {
        var userId = Guid.NewGuid();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.GrantInTenant(userId, tenant1, ReadPerm);
            opts.GrantInTenant(userId, tenant2, CreatePerm);
        });

        var result1 = await store.GetPermissionsAsync(userId, tenant1);
        var result2 = await store.GetPermissionsAsync(userId, tenant2);

        result1.IsSuccess.ShouldBeTrue();
        result1.Value.ShouldContain(ReadPerm);
        result1.Value.ShouldNotContain(CreatePerm);

        result2.IsSuccess.ShouldBeTrue();
        result2.Value.ShouldContain(CreatePerm);
        result2.Value.ShouldNotContain(ReadPerm);
    }

    // ── Isolation between system and tenant grants ─────────────────────────

    [Fact]
    public async Task GetPermissionsAsync_TenantScoped_DoesNotReturnSystemGrantsForSameUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.Grant(userId, ReadPerm);          // system grant
            opts.GrantInTenant(userId, tenantId, CreatePerm); // tenant grant
        });

        var tenantResult = await store.GetPermissionsAsync(userId, tenantId);

        tenantResult.IsSuccess.ShouldBeTrue();
        tenantResult.Value.ShouldContain(CreatePerm);
        tenantResult.Value.ShouldNotContain(ReadPerm);
    }

    [Fact]
    public async Task GetPermissionsAsync_SystemLevel_DoesNotReturnTenantGrantsForSameUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.Grant(userId, ReadPerm);
            opts.GrantInTenant(userId, tenantId, CreatePerm);
        });

        var systemResult = await store.GetPermissionsAsync(userId);

        systemResult.IsSuccess.ShouldBeTrue();
        systemResult.Value.ShouldContain(ReadPerm);
        systemResult.Value.ShouldNotContain(CreatePerm);
    }
}
