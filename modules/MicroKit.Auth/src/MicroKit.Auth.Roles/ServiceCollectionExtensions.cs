namespace MicroKit.Auth.Roles;

/// <summary>
/// DI registration extensions for <c>MicroKit.Auth.Roles</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RoleRegistry"/> as a singleton with optional startup configuration.
    /// </summary>
    /// <remarks>
    /// Idempotent — uses <c>TryAddSingleton</c> so calling this method more than once has no effect.
    /// The factory runs once when the singleton is first resolved.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">
    /// Optional action to populate the registry at startup.
    /// If <see langword="null"/>, an empty registry is registered.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRoleRegistry(
        this IServiceCollection services,
        Action<RoleRegistry>? configure = null)
    {
        services.TryAddSingleton(_ =>
        {
            var registry = new RoleRegistry();
            configure?.Invoke(registry);
            return registry;
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryRoleStore"/> as the singleton <see cref="IRoleStore"/>,
    /// replacing any previously registered store (including the <c>NullRoleStore</c> registered
    /// by <c>AddMicroKitAuthCore()</c>).
    /// Optionally also registers <see cref="InMemoryRolePermissionMap"/> as the singleton
    /// <see cref="IRolePermissionMap"/>, replacing the <c>NullRolePermissionMap</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Uses <c>services.Replace()</c>.</strong> <c>AddMicroKitAuthCore()</c> registers
    /// <c>NullRoleStore</c> as a <em>scoped</em> service via <c>TryAddScoped</c>. Replacing it
    /// with <c>AddSingleton</c> or <c>TryAddSingleton</c> would leave the scoped descriptor in place,
    /// causing a lifetime mismatch when <see cref="IRoleStore"/> is resolved from a scoped
    /// container. <c>Replace()</c> removes the existing descriptor entirely before registering the
    /// singleton, ensuring a single, consistent registration with the correct lifetime.
    /// <para>
    /// Call this method after <c>AddMicroKitAuth()</c> or <c>AddMicroKitAuthCore()</c>.
    /// Optionally call <c>AddPermissionRegistry()</c> separately if a permission catalog is needed.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Action to configure user-to-role assignments. Must not be <see langword="null"/>.</param>
    /// <param name="configureMap">
    /// Optional action to configure role-to-permission mappings.
    /// When provided, replaces <c>NullRolePermissionMap</c> with <see cref="InMemoryRolePermissionMap"/>.
    /// When <see langword="null"/>, the existing <see cref="IRolePermissionMap"/> registration is unchanged.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddInMemoryRoles(
        this IServiceCollection services,
        Action<InMemoryRoleStoreOptions> configure,
        Action<InMemoryRolePermissionMapOptions>? configureMap = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Replace() removes the existing descriptor entirely (including NullRoleStore registered
        // as scoped by AddMicroKitAuthCore) before adding the singleton. This prevents the lifetime
        // mismatch that would occur if a scoped and a singleton descriptor coexisted for IRoleStore.
        services.Replace(ServiceDescriptor.Singleton<IRoleStore>(_ =>
        {
            var options = new InMemoryRoleStoreOptions();
            configure(options);
            return new InMemoryRoleStore(options);
        }));

        if (configureMap is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IRolePermissionMap>(_ =>
            {
                var options = new InMemoryRolePermissionMapOptions();
                configureMap(options);
                return new InMemoryRolePermissionMap(options);
            }));
        }

        return services;
    }
}
