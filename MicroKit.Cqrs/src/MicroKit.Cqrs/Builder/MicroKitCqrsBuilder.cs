using Autofac;
using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.Cqrs.Cache;

namespace MicroKit.Cqrs.Builder;

/// <summary>Builder for configuring and registering MicroKit CQRS services into an Autofac container.</summary>
public class MicroKitCqrsBuilder
{
    /// <summary>Gets the underlying Autofac container builder.</summary>
    public ContainerBuilder Builder { get; }
    /// <summary>Gets the options used to configure CQRS services.</summary>
    public MicroKitCqrsBuilderOptions Options { get; }
    /// <summary>Initializes a new instance with the given Autofac <paramref name="containerBuilder"/>.</summary>
    /// <param name="containerBuilder">The Autofac container builder to register services into.</param>
    public MicroKitCqrsBuilder(ContainerBuilder containerBuilder)
    {
        Builder = containerBuilder;
        Options = new();
    }

    /// <summary>Applies custom configuration to the CQRS options.</summary>
    /// <param name="options">Delegate that configures <see cref="MicroKitCqrsBuilderOptions"/>.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public MicroKitCqrsBuilder Configure(Action<MicroKitCqrsBuilderOptions> options)
    {
        options?.Invoke(Options);
        return this;
    }
    /// <summary>
    /// Enregistre les services de base pour le caching (Clés, Eligibilité, etc.)
    /// </summary>
    public MicroKitCqrsBuilder Build()
    {
        RegisterCache();
        return this;
    }

    /// <summary>
    /// Registers the cache.
    /// </summary>
    private void RegisterCache()
    {
        // Stratégie de clé (Default)
        Builder.RegisterType<DefaultCacheKeyService>()
            .As<ICacheKeyService>()
            .InstancePerLifetimeScope() // Scoped est souvent préférable pour le cache
            .IfNotRegistered(typeof(ICacheKeyService));

        // Stratégie d'éligibilité (Default)
        Builder.RegisterType<DefaultCacheEligibilityChecker>()
            .As<ICacheEligibilityChecker>()
            .InstancePerLifetimeScope()
            .IfNotRegistered(typeof(ICacheEligibilityChecker));
    }
}
