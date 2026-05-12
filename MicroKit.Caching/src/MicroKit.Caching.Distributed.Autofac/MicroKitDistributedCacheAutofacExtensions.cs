using Autofac;
using MicroKit.Caching.Abstractions;

namespace MicroKit.Caching.Distributed.Autofac;

/// <summary>
/// Autofac registration helpers for <c>MicroKit.Caching.Distributed</c>.
/// This package is a thin Autofac adapter — it wires <see cref="DistributedCacheService"/>
/// (from <c>MicroKit.Caching.Distributed</c>) as the singleton <see cref="ICacheService"/>
/// inside an Autofac container. Use the Microsoft DI extension in
/// <c>MicroKit.Caching.Distributed</c> directly when Autofac is not in use.
/// </summary>
public static class MicroKitDistributedCacheAutofacExtensions
{
    /// <summary>
    /// Registers <see cref="DistributedCacheService"/> as the singleton <see cref="ICacheService"/>
    /// if no other implementation is already registered. Safe to call multiple times.
    /// </summary>
    /// <param name="builder">The Autofac container builder.</param>
    public static ContainerBuilder RegisterMicroKitDistributedCache(this ContainerBuilder builder)
    {
        builder.RegisterType<DistributedCacheService>()
               .As<ICacheService>()
               .SingleInstance()
               .IfNotRegistered(typeof(ICacheService));

        return builder;
    }
}
