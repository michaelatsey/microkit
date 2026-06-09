namespace MicroKit.Auth.Permissions;

/// <summary>
/// DI registration extensions for <c>MicroKit.Auth.Permissions</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PermissionRegistry"/> as a singleton with optional startup configuration.
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
    public static IServiceCollection AddPermissionRegistry(
        this IServiceCollection services,
        Action<PermissionRegistry>? configure = null)
    {
        services.TryAddSingleton(_ =>
        {
            var registry = new PermissionRegistry();
            configure?.Invoke(registry);
            return registry;
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryPermissionStore"/> as the singleton <see cref="IPermissionStore"/>,
    /// replacing any previously registered store (including the <c>NullPermissionStore</c> registered
    /// by <c>AddMicroKitAuthCore()</c>).
    /// </summary>
    /// <remarks>
    /// <strong>Uses <c>services.Replace()</c>.</strong> <c>AddMicroKitAuthCore()</c> registers
    /// <c>NullPermissionStore</c> as a <em>scoped</em> service via <c>TryAddScoped</c>. Replacing it
    /// with <c>AddSingleton</c> or <c>TryAddSingleton</c> would leave the scoped descriptor in place,
    /// causing a lifetime mismatch when <see cref="IPermissionStore"/> is resolved from a scoped
    /// container. <c>Replace()</c> removes the existing descriptor entirely before registering the
    /// singleton, ensuring a single, consistent registration with the correct lifetime.
    /// <para>
    /// Call this method after <c>AddMicroKitAuth()</c> or <c>AddMicroKitAuthCore()</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Action to configure user-to-permission mappings. Must not be <see langword="null"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddInMemoryPermissions(
        this IServiceCollection services,
        Action<InMemoryPermissionStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Replace() removes the existing descriptor entirely (including NullPermissionStore registered
        // as scoped by AddMicroKitAuthCore) before adding the singleton. This prevents the lifetime
        // mismatch that would occur if a scoped and a singleton descriptor coexisted for IPermissionStore.
        services.Replace(ServiceDescriptor.Singleton<IPermissionStore>(_ =>
        {
            var options = new InMemoryPermissionStoreOptions();
            configure(options);
            return new InMemoryPermissionStore(options);
        }));

        return services;
    }
}
