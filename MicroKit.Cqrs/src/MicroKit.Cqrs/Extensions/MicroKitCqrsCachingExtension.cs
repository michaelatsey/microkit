using MicroKit.Cqrs.Abstractions.Cache;
using MicroKit.Cqrs.Builder;
using MicroKit.Cqrs.Cache;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Cqrs.Extensions;

//public static class MicroKitCqrsCachingExtension
//{
//    public static MicroKitCqrsBuilder AddMicroKitCaching(this MicroKitCqrsBuilder builder)
//    {
//        // On enregistre des implémentations par défaut (si l'utilisateur n'en fournit pas)
//        builder.Services.TryAddTransient<ICacheKeyService, DefaultCacheKeyService>();
//        builder.Services.TryAddTransient<ICacheEligibilityChecker, DefaultCacheEligibilityChecker>();

//        return builder;
//    }
//}
