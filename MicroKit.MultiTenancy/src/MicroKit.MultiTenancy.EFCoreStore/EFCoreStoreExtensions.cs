using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.MultiTenancy.EFCoreStore;

/// <summary>Extension methods for registering the EF Core-backed tenant store.</summary>
public static class TenantStoreExtensions
{
    /// <summary>Registers <see cref="EFCoreTenantStore{TContext}"/> as the active <see cref="ITenantStore"/> and <see cref="ITenantRegistry"/>.</summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> that contains the <c>Tenant</c> entity.</typeparam>
    /// <param name="builder">The multi-tenancy builder.</param>
    /// <param name="configureOptions">Optional callback to configure database tenant options.</param>
    /// <param name="services">Optional callback to override service registrations after the defaults are applied.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MicroKitMultiTenantBuilder WithDatabaseStore<TContext>(
        this MicroKitMultiTenantBuilder builder,
        Action<DatabaseTenantOptions>? configureOptions = null,
        Action<IServiceCollection>? services = null) // Le "hook" pour l'override
        where TContext : DbContext
    {
        // 1. Options standard
        builder.Services.Configure(configureOptions ?? (_ => { }));

        // 2. Enregistrement du Store
        builder.Services.AddScoped<EFCoreTenantStore<TContext>>();

        // 3. Liaison par défaut
        ReplaceResolver<EFCoreTenantStore<TContext>>(builder);
        //ReplaceResolver<ITenantStore>(builder, sp => sp.GetRequiredService<EFCoreTenantStore<TContext>>());
        //ReplaceResolver<ITenantRegistry>(builder, sp => sp.GetRequiredService<EFCoreTenantStore<TContext>>());

        // 4. Exécution de l'override utilisateur (s'il existe)
        // C'est ici que l'utilisateur peut écraser ITenantRegistry par exemple
        services?.Invoke(builder.Services);

        return builder;
    }

    private static void ReplaceResolver<TImplementation>(MicroKitMultiTenantBuilder builder)
        where TImplementation : class, ITenantStore
    {
        // 1. On nettoie les anciens enregistrements pour éviter les conflits (Override)
        builder.Services.RemoveAll<ITenantStore>();
        builder.Services.RemoveAll<ITenantRegistry>();
        builder.Services.RemoveAll<TImplementation>();

        // 2. On enregistre la classe concrète elle-même
        builder.Services.AddScoped<TImplementation>();

        // 3. On lie ITenantStore à cette instance unique
        builder.Services.AddScoped<ITenantStore>(sp => sp.GetRequiredService<TImplementation>());

        // 4. On lie ITenantRegistry SI l'implémentation supporte l'interface
        if (typeof(ITenantRegistry).IsAssignableFrom(typeof(TImplementation)))
        {
            builder.Services.AddScoped<ITenantRegistry>(sp => (ITenantRegistry)sp.GetRequiredService<TImplementation>());
        }
    }

   ///// <summary>
    ///// Remplace le service existant ou en ajoute un nouveau avec une factory.
    ///// </summary>
    //private static void ReplaceResolver<TContrat>(
    //    MicroKitMultiTenantBuilder builder,
    //    Func<IServiceProvider, TContrat> factory)
    //    where TContrat : class
    //{
    //    // On retire toutes les implémentations précédentes pour permettre l'override
    //    var descriptors = builder.Services
    //        .Where(x => x.ServiceType == typeof(TContrat))
    //        .ToList();

    //    foreach (var d in descriptors)
    //        builder.Services.Remove(d);

    //    // On ajoute la nouvelle résolution
    //    builder.Services.AddScoped<TContrat>(factory);
    //}
}
