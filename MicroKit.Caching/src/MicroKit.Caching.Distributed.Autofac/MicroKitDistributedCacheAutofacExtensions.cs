using Autofac;
using MicroKit.Caching.Abstractions;

namespace MicroKit.Caching.Distributed.Autofac;

public static class MicroKitDistributedCacheAutofacExtensions
{
    public static ContainerBuilder RegisterMicroKitDistributedCache(this ContainerBuilder builder)
    {
        builder.RegisterType<DistributedCacheService>()
               .As<ICacheService>()
               .SingleInstance() // Singleton
               .IfNotRegistered(typeof(ICacheService));

        return builder;
    }
}
