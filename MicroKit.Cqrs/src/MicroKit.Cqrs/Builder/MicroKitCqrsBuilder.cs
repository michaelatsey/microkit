using Autofac;
using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.Cqrs.Cache;

namespace MicroKit.Cqrs.Builder;

public class MicroKitCqrsBuilder
{
    public ContainerBuilder Builder { get; }
    public MicroKitCqrsBuilderOptions Options { get; }
    public MicroKitCqrsBuilder(ContainerBuilder containerBuilder)
    {
        Builder = containerBuilder;
        Options = new();
    }

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
