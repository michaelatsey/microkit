using MicroKit.MultiTenancy.Abstractions;
using MicroKit.MultiTenancy.Configuration;
using MicroKit.MultiTenancy.RegionResolvers;
using MicroKit.MultiTenancy.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.MultiTenancy.Extensions;

public static class TenantStoreExtensions
{

    public static MicroKitMultiTenantBuilder WithPassThroughTenantStore(
        this MicroKitMultiTenantBuilder builder,
        Action<PassThroughTenantOptions>? configureOptions = null,
        string configSectionPath = "MicroKit:MultiTenancy:Store:PassThrough")
    {
        // Guard clauses
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Enregistrement des options
        if (configureOptions != null)
        {
            builder.Services.Configure(configureOptions);
        }
        else
        {
            // On essaie de lier la section par défaut du appsettings.json
            builder.Services.AddOptions<PassThroughTenantOptions>()
                .BindConfiguration(configSectionPath)
                .ValidateDataAnnotations() // Si vous utilisez des annotations
                .ValidateOnStart(); // Valider au démarrage;

            // Validation complémentaire
            builder.Services.AddOptions<PassThroughTenantOptions>()
                .PostConfigure(options =>
                {
                    // S'assurer que StaticTenants n'est pas null
                    options.StaticTenants ??= [];

                    // Normaliser les IDs (optionnel)
                    options.StaticTenants = [.. options.StaticTenants
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)];
                });
        }

        ReplaceResolver<PassThroughTenantStore>(builder);

        // Logging de diagnostic
        LogRegistrationDiagnostic(builder.Services, "PassThroughTenantStore", configSectionPath);
        return builder;
    }
    

    public static MicroKitMultiTenantBuilder WithRemoteStore(
        this MicroKitMultiTenantBuilder builder,
        Action<RemoteTenantOptions>? configureOptions = null,
        string configSectionPath = RemoteTenantOptions.SectionName)
    {
        builder.Services
            .AddOptions<RemoteTenantOptions>()
            .BindConfiguration(configSectionPath)
            .ValidateDataAnnotations()
            .ValidateOnStart(); ;

        if (configureOptions != null)
        {
            builder.Services.Configure(configureOptions);
        }

        builder.Services.AddHttpClient("TenantService", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<RemoteTenantOptions>>().Value;
            if (options.BaseAddress == null)
                throw new InvalidOperationException("RemoteTenantStore: BaseAddress is required.");
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        // Ajout automatique de la résilience native .NET (Retry, Circuit Breaker)
        .AddStandardResilienceHandler();

        // Configuration de l'endpoint provider pour construire les URL d'API
        ReplaceResolver<RemoteTenantStore>(builder);

        builder.Services.AddScoped<ClaimsTenantRegionResolver>();
        builder.Services.AddScoped<DatabaseTenantRegionResolver>();
        builder.Services.AddScoped<ConfigurationTenantRegionResolver>();

        LogRegistrationDiagnostic(builder.Services, "RemoteTenantStore", configSectionPath);
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

    private static void LogRegistrationDiagnostic(IServiceCollection services, string tenantStoreType,  string configSectionPath)
    {
        // Option : Logger temporaire
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().AddDebug());

        var logger = loggerFactory.CreateLogger("MicroKit.MultiTenancy");
        if (logger is not null && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "{TenantStoreType} registered with config section: {ConfigPath}",
                tenantStoreType,
                configSectionPath);
        }
    }

    //private static void ReplaceResolver<TResolver>(MicroKitMultiTenantBuilder builder)
    //    where TResolver : class, ITenantStore
    //{
    //    var descriptor = builder.Services
    //        .FirstOrDefault(x => x.ServiceType == typeof(ITenantStore));

    //    if (descriptor != null)
    //        builder.Services.Remove(descriptor);

    //    builder.Services.AddScoped<ITenantStore, TResolver>();
    //}
}
