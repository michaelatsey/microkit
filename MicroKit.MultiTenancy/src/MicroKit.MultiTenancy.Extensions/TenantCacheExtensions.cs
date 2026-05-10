using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Cache;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.MultiTenancy.Extensions;

public static class TenantCacheExtensions
{
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
