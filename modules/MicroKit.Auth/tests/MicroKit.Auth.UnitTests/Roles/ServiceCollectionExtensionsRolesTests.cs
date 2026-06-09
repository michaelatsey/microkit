namespace MicroKit.Auth.UnitTests.Roles;

public sealed class ServiceCollectionExtensionsRolesTests
{
    // ── AddRoleRegistry ───────────────────────────────────────────────────

    [Fact]
    public void AddRoleRegistry_WhenCalledWithNoConfig_RegistersEmptyRegistry()
    {
        var services = new ServiceCollection();
        services.AddRoleRegistry();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<RoleRegistry>();

        registry.All.ShouldBeEmpty();
    }

    [Fact]
    public void AddRoleRegistry_WhenCalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddRoleRegistry(reg => reg.Register(SystemRoles.Admin));
        services.AddRoleRegistry(reg => reg.Register(SystemRoles.Viewer)); // second call ignored

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<RoleRegistry>();

        registry.All.Count.ShouldBe(1);
        registry.Contains(SystemRoles.Admin).ShouldBeTrue();
    }

    [Fact]
    public void AddRoleRegistry_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddRoleRegistry();
        var provider = services.BuildServiceProvider();

        var registry1 = provider.GetRequiredService<RoleRegistry>();
        var registry2 = provider.GetRequiredService<RoleRegistry>();

        registry1.ShouldBeSameAs(registry2);
    }

    // ── AddInMemoryRoles ──────────────────────────────────────────────────

    [Fact]
    public void AddInMemoryRoles_WhenCalledWithNullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            services.AddInMemoryRoles(null!));
    }

    [Fact]
    public void AddInMemoryRoles_ReplacesExistingIRoleStoreDescriptor()
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryRoles(_ => { });

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IRoleStore))
            .ToList();

        descriptors.Count.ShouldBe(1);
        descriptors[0].Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInMemoryRoles_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryRoles(_ => { });
        var provider = services.BuildServiceProvider();

        var store1 = provider.GetRequiredService<IRoleStore>();
        var store2 = provider.GetRequiredService<IRoleStore>();

        store1.ShouldBeSameAs(store2);
    }

    [Fact]
    public async Task AddInMemoryRoles_WhenResolved_UsesConfiguredMappings()
    {
        var userId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryRoles(opts => opts.Grant(userId, SystemRoles.Admin));
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IRoleStore>();

        var result = await store.GetRolesAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(SystemRoles.Admin);
    }

    [Fact]
    public void AddInMemoryRoles_WithConfigureMap_ReplacesIRolePermissionMapDescriptor()
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryRoles(_ => { }, configureMap: _ => { });

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IRolePermissionMap))
            .ToList();

        descriptors.Count.ShouldBe(1);
        descriptors[0].Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddInMemoryRoles_WithoutConfigureMap_DoesNotReplaceIRolePermissionMap()
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryRoles(_ => { }); // no configureMap

        // IRolePermissionMap descriptor is the NullRolePermissionMap (singleton) from Core
        var descriptor = services.Single(d => d.ServiceType == typeof(IRolePermissionMap));

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }
}
