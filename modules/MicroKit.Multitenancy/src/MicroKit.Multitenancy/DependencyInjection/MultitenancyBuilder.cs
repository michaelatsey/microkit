namespace MicroKit.Multitenancy;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Fluent builder for configuring MicroKit.Multitenancy services.
/// Returned by <see cref="MultitenancyServiceCollectionExtensions.AddMicroKitMultitenancy"/>.
/// </summary>
/// <remarks>
/// Store registration behavior: each <c>Use*Store</c> call appends a new <see cref="ITenantStore"/>
/// registration. The .NET DI container resolves the <b>last</b> registered implementation.
/// Calling multiple store methods in sequence is therefore safe — the last call wins.
/// </remarks>
public sealed class MultitenancyBuilder(IServiceCollection services)
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services => services;

    /// <summary>
    /// Registers <see cref="InMemoryTenantStore"/> as the active <see cref="ITenantStore"/>.
    /// </summary>
    /// <param name="tenants">Optional seed tenants pre-loaded into the store.</param>
    /// <returns>This builder for chaining.</returns>
    public MultitenancyBuilder UseInMemoryStore(IEnumerable<ITenantInfo>? tenants = null)
    {
        if (tenants is null)
            services.AddSingleton<ITenantStore, InMemoryTenantStore>();
        else
            services.AddSingleton<ITenantStore>(new InMemoryTenantStore(tenants));
        return this;
    }

    /// <summary>
    /// Registers <see cref="ConfigurationTenantStore"/> as the active <see cref="ITenantStore"/>.
    /// </summary>
    /// <remarks>
    /// The caller must bind <see cref="MultitenancyOptions"/> separately:
    /// <code>
    /// services.Configure&lt;MultitenancyOptions&gt;(config.GetSection(MultitenancyOptions.SectionKey));
    /// </code>
    /// </remarks>
    /// <returns>This builder for chaining.</returns>
    public MultitenancyBuilder UseConfigurationStore()
    {
        services.AddSingleton<ITenantStore, ConfigurationTenantStore>();
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantStore"/> implementation as Singleton.
    /// </summary>
    /// <typeparam name="TStore">The store implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public MultitenancyBuilder UseStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, ITenantStore
    {
        services.AddSingleton<ITenantStore, TStore>();
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantStore"/> instance as Singleton.
    /// </summary>
    /// <typeparam name="TStore">The store implementation type.</typeparam>
    /// <param name="instance">The pre-constructed store instance.</param>
    /// <returns>This builder for chaining.</returns>
    public MultitenancyBuilder UseStore<TStore>(TStore instance) where TStore : class, ITenantStore
    {
        services.AddSingleton<ITenantStore>(instance);
        return this;
    }
}
