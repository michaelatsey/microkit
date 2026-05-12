using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Redis.Extensions;

/// <summary>Extension methods for wiring up <see cref="RedisTenantCache"/> in the DI container.</summary>
public static class TenantCacheExtensions
{
    /// <summary>
    /// Registers <see cref="RedisTenantCache"/> as the <see cref="ITenantCache"/> implementation.
    /// </summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisTenantCacheOptions"/>.</param>
    public static MicroKitMultiTenantBuilder WithRedisCache(
        this MicroKitMultiTenantBuilder builder,
        Action<RedisTenantCacheOptions>? configure = null)
    {
        builder.Services.AddOptions<RedisTenantCacheOptions>();
        if (configure is not null)
            builder.Services.Configure(configure);

        ReplaceResolver<RedisTenantCache>(builder);
        return builder;
    }

    private static void ReplaceResolver<TResolver>(MicroKitMultiTenantBuilder builder)
        where TResolver : class, ITenantCache
    {
        var descriptor = builder.Services
            .FirstOrDefault(x => x.ServiceType == typeof(ITenantCache));

        if (descriptor != null)
            builder.Services.Remove(descriptor);

        builder.Services.AddSingleton<ITenantCache, TResolver>();
    }
}
