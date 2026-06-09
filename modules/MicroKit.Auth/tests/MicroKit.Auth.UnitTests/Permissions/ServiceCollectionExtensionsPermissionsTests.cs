namespace MicroKit.Auth.UnitTests.Permissions;

public sealed class ServiceCollectionExtensionsPermissionsTests
{
    private static readonly Permission ReadPerm = Permission.Of("docs", "read");
    private static readonly Permission CreatePerm = Permission.Of("docs", "create");

    // ── AddPermissionRegistry ─────────────────────────────────────────────

    [Fact]
    public void AddPermissionRegistry_WhenCalledWithNullConfigure_RegistersEmptyRegistry()
    {
        var services = new ServiceCollection();
        services.AddPermissionRegistry();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<PermissionRegistry>();

        registry.All.ShouldBeEmpty();
    }

    [Fact]
    public void AddPermissionRegistry_WhenCalledWithConfigure_RegistersWithPermissions()
    {
        var services = new ServiceCollection();
        services.AddPermissionRegistry(reg => reg.Register(ReadPerm, CreatePerm));
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<PermissionRegistry>();

        registry.All.Count.ShouldBe(2);
        registry.Contains(ReadPerm).ShouldBeTrue();
        registry.Contains(CreatePerm).ShouldBeTrue();
    }

    [Fact]
    public void AddPermissionRegistry_WhenCalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddPermissionRegistry(reg => reg.Register(ReadPerm));
        services.AddPermissionRegistry(reg => reg.Register(CreatePerm)); // second call ignored
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<PermissionRegistry>();

        registry.All.Count.ShouldBe(1);
        registry.Contains(ReadPerm).ShouldBeTrue();
    }

    [Fact]
    public void AddPermissionRegistry_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddPermissionRegistry();
        var provider = services.BuildServiceProvider();

        var registry1 = provider.GetRequiredService<PermissionRegistry>();
        var registry2 = provider.GetRequiredService<PermissionRegistry>();

        registry1.ShouldBeSameAs(registry2);
    }

    // ── AddInMemoryPermissions ────────────────────────────────────────────

    [Fact]
    public void AddInMemoryPermissions_RegistersIPermissionStoreAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryPermissions(_ => { });
        var provider = services.BuildServiceProvider();

        var store1 = provider.GetRequiredService<IPermissionStore>();
        var store2 = provider.GetRequiredService<IPermissionStore>();

        store1.ShouldBeSameAs(store2);
    }

    [Fact]
    public void AddInMemoryPermissions_WhenCalledAfterAddMicroKitAuthCore_ReplacesNullPermissionStore()
    {
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryPermissions(_ => { });

        // Only one IPermissionStore descriptor should exist — the InMemoryPermissionStore singleton
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IPermissionStore))
            .ToList();

        descriptors.Count.ShouldBe(1);
        descriptors[0].Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task AddInMemoryPermissions_WhenResolved_UsesConfiguredMappings()
    {
        var userId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddMicroKitAuthCore();
        services.AddInMemoryPermissions(opts => opts.Grant(userId, ReadPerm));
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IPermissionStore>();

        var result = await store.GetPermissionsAsync(userId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(ReadPerm);
    }

    [Fact]
    public void AddInMemoryPermissions_WhenConfigureIsNull_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            services.AddInMemoryPermissions(null!));
    }
}
