using MicroKit.Abstractions.Configuration;
using MicroKit.Caching.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Caching.Distributed;

public static class MicroKitCachingBuilderExtensions
{
    public static MicroKitBuilder AddMicroKitDistributedCache(this MicroKitBuilder builder)
    {
        // 1. Protection : Si déjà enregistré, on ne fait rien
        if (builder.Services.Any(d => d.ServiceType == typeof(ICacheService)))
            return builder;
        // On enregistre notre implémentation qui wrap IDistributedCache
        builder.Services.TryAddSingleton<ICacheService, DistributedCacheService>();

        // On n'enregistre PAS de fournisseur IDistributedCache ici !
        // C'est à l'utilisateur de choisir : services.AddStackExchangeRedisCache(...) 
        // ou services.AddDistributedMemoryCache().

        return builder;
    }

    public static IServiceCollection AddMicroKitDistributedCache(this IServiceCollection services)
    {
        // 1. Protection : Si déjà enregistré, on ne fait rien
        if (services.Any(d => d.ServiceType == typeof(ICacheService)))
            return services;
        // On enregistre notre implémentation qui wrap IDistributedCache
        services.TryAddSingleton<ICacheService, DistributedCacheService>();

        // On n'enregistre PAS de fournisseur IDistributedCache ici !
        // C'est à l'utilisateur de choisir : services.AddStackExchangeRedisCache(...) 
        // ou services.AddDistributedMemoryCache().

        return services;
    }
}
