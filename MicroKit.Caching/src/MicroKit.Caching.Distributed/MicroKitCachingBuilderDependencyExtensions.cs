using MicroKit.Abstractions.Configuration;
using MicroKit.Caching.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Caching.Distributed;

public static class MicroKitCachingBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="DistributedCacheService"/> as the <see cref="ICacheService"/> implementation.
    /// Call <c>services.AddStackExchangeRedisCache(…)</c> or <c>services.AddDistributedMemoryCache()</c>
    /// separately to supply the underlying <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
    /// </summary>
    public static MicroKitBuilder AddMicroKitDistributedCache(
        this MicroKitBuilder builder,
        Action<DistributedCacheOptions>? configure = null)
    {
        if (builder.Services.Any(d => d.ServiceType == typeof(ICacheService)))
            return builder;

        builder.Services.AddOptions<DistributedCacheOptions>();
        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.TryAddSingleton<ICacheService, DistributedCacheService>();

        return builder;
    }

    /// <summary>
    /// Registers <see cref="DistributedCacheService"/> as the <see cref="ICacheService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMicroKitDistributedCache(
        this IServiceCollection services,
        Action<DistributedCacheOptions>? configure = null)
    {
        if (services.Any(d => d.ServiceType == typeof(ICacheService)))
            return services;

        services.AddOptions<DistributedCacheOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton<ICacheService, DistributedCacheService>();

        return services;
    }
}
