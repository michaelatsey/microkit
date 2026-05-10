using Autofac;
using MicroKit.Caching.Abstractions;
using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.Sample.OrderApi.Infrastructure.Caching;

namespace MicroKit.Sample.OrderApi.Infrastructure.Modules
{
    public class CachingModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // 1. Enregistrement de la stratégie de clé (connaît le Tenant)
            builder.RegisterType<TenantCacheKeyService>()
                .As<ICacheKeyService>()
                .InstancePerLifetimeScope();

            // 2. Enregistrement du Checker (connaît Ardalis)
            builder.RegisterType<ArdalisCacheEligibilityChecker>()
                .As<ICacheEligibilityChecker>()
                .SingleInstance();

            // 3. Enregistrement de ton moteur technique (Redis, etc.)
            builder.RegisterType<DistributedCacheService>()
                .As<ICacheService>()
                .SingleInstance();

        }
    }
}
