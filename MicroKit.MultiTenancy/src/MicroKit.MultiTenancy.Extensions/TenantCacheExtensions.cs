using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Extensions;

/// <summary>Extension methods for configuring the tenant cache implementation.</summary>
public static class TenantCacheExtensions
{
    /// <summary>Replaces the default tenant cache with an in-memory implementation.</summary>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMultiTenantBuilder WithInMemoryCache(this MicroKitMultiTenantBuilder builder)
    {
        ReplaceResolver<DefaultTenantCache>(builder);
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
