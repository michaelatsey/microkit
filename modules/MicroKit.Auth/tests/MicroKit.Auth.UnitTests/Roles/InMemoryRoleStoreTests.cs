namespace MicroKit.Auth.UnitTests.Roles;

public sealed class InMemoryRoleStoreTests
{
    private static IRoleStore CreateStore(Action<InMemoryRoleStoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryRoles(opts => configure?.Invoke(opts));
        return services.BuildServiceProvider().GetRequiredService<IRoleStore>();
    }

    // ── System-level (Grant / GetRolesAsync(userId)) ──────────────────────

    [Fact]
    public async Task GetRolesAsync_WhenUserHasSystemGrant_ReturnsGrantedRoles()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(opts => opts.Grant(userId, SystemRoles.Admin));

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(SystemRoles.Admin);
    }

    [Fact]
    public async Task GetRolesAsync_WhenUserHasNoGrant_ReturnsEmptyList()
    {
        var store = CreateStore();

        var result = await store.GetRolesAsync(Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Grant_WhenCalledTwice_AccumulatesRoles()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.Grant(userId, SystemRoles.Admin);
            opts.Grant(userId, SystemRoles.Auditor);
        });

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(SystemRoles.Admin);
        result.Value.ShouldContain(SystemRoles.Auditor);
    }

    // ── Tenant-scoped (GrantInTenant / GetRolesAsync(userId, tenantId)) ───

    [Fact]
    public async Task GetRolesAsync_WithTenant_WhenGrantExists_ReturnsGrantedRoles()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore(opts => opts.GrantInTenant(userId, tenantId, SystemRoles.Manager));

        var result = await store.GetRolesAsync(userId, tenantId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(SystemRoles.Manager);
    }

    [Fact]
    public async Task GetRolesAsync_WithTenant_WhenNoGrant_ReturnsEmptyList()
    {
        var store = CreateStore();

        var result = await store.GetRolesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GrantInTenant_WhenGrantedForDifferentTenants_ReturnsCorrectRoles()
    {
        var userId = Guid.NewGuid();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.GrantInTenant(userId, tenant1, SystemRoles.Admin);
            opts.GrantInTenant(userId, tenant2, SystemRoles.Viewer);
        });

        var result1 = await store.GetRolesAsync(userId, tenant1);
        var result2 = await store.GetRolesAsync(userId, tenant2);

        result1.Value.ShouldContain(SystemRoles.Admin);
        result1.Value.ShouldNotContain(SystemRoles.Viewer);
        result2.Value.ShouldContain(SystemRoles.Viewer);
        result2.Value.ShouldNotContain(SystemRoles.Admin);
    }

    // ── Isolation between system and tenant grants ─────────────────────────

    [Fact]
    public async Task SystemAndTenantGrants_AreIsolated()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var store = CreateStore(opts =>
        {
            opts.Grant(userId, SystemRoles.Admin);
            opts.GrantInTenant(userId, tenantId, SystemRoles.Viewer);
        });

        var systemResult = await store.GetRolesAsync(userId);
        var tenantResult = await store.GetRolesAsync(userId, tenantId);

        systemResult.Value.ShouldContain(SystemRoles.Admin);
        systemResult.Value.ShouldNotContain(SystemRoles.Viewer);
        tenantResult.Value.ShouldContain(SystemRoles.Viewer);
        tenantResult.Value.ShouldNotContain(SystemRoles.Admin);
    }
}
