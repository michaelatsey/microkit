using MicroKit.Abstractions.Configuration;
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Core.Accessors;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.Core.Context;
using MicroKit.Idempotency.Core.Hashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Idempotency.Core;

public static class ServiceCollectionExtensions
{
    public static MicroKitBuilder AddMicroKitIdempotency(
        this MicroKitBuilder builder,
        Action<MicroKitIdempotencyBuilder>? configure = null)
    {
        var services = builder.Services;
        services
            .AddOptions<IdempotencyOptions>()
            .BindConfiguration("MicroKit:Idempotency") // Optionnel : permet de binder depuis appsettings.json
            //.Configure(options => configure?.Invoke(options))
            .ValidateDataAnnotations() // Active les attributs [Range], [Required], etc.
            .ValidateOnStart();

        var innerBuilder = new MicroKitIdempotencyBuilder(services);
        configure?.Invoke(innerBuilder);

        services.AddCoreServices();

        return builder;
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.TryAddScoped<IIdempotencyContext, IdempotencyContext>(); // Ajout du contexte
        services.TryAddSingleton<RequestHasher>();
        // 1. Enregistrer l'implémentation concrète en Scoped
        services.AddScoped<IdempotencyProvider>();

        // 2. Faire pointer les deux interfaces vers la même instance résolue
        services.AddScoped<IIdempotencyAccessor>(sp => sp.GetRequiredService<IdempotencyProvider>());
        services.AddScoped<IIdempotencyManager>(sp => sp.GetRequiredService<IdempotencyProvider>());
        services.AddHostedService<IdempotencyCleanupWorker>();
        return services;
    }
}
