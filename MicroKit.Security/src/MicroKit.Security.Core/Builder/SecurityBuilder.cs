
using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Security.Core.Builder;
/// <summary>
/// Builder for configuring MicroKit.Security services.
/// </summary>
public sealed class SecurityBuilder(IServiceCollection services)
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Configured default authentication scheme.
    /// </summary>
    public AuthenticationScheme DefaultScheme { get; set; } = AuthenticationScheme.ApiKey;

    /// <summary>
    /// Enregistre un fournisseur d'authentification avec option de cache.
    /// </summary>
    public SecurityBuilder AddProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        string schemeName, // <-- AJOUT DU NOM DU SCHEME
        bool enableCache = false)
        where TProvider : class, IAuthenticationProvider
        where TOptions : class, ICacheableOptions
    {
        // 1. Enregistrement de l'implémentation concrète
        Services.TryAddScoped<TProvider>();

        // 2. Enregistrement du service en tant que IAuthenticationProvider
        if (enableCache)
        {
            // On lie les CacheOptions NOMMÉES aux options PARENTES
            // C'est ici que la magie opère : pas de PostConfigure manuel
            Services.AddOptions<CacheOptions>(schemeName)
                .Configure<IOptionsMonitor<TOptions>>((cacheOpt, parentMonitor) =>
                {
                    var source = parentMonitor.Get(schemeName).Cache;
                    // On copie les valeurs proprement
                    cacheOpt.Enabled = source.Enabled;
                    cacheOpt.KeyPrefix = source.KeyPrefix;
                    cacheOpt.SuccessDurationSeconds = source.SuccessDurationSeconds;
                    cacheOpt.FailureDurationSeconds = source.FailureDurationSeconds;
                    cacheOpt.DefaultDurationSeconds = source.DefaultDurationSeconds;
                    cacheOpt.MaxCacheSize = source.MaxCacheSize;
                    cacheOpt.UseSlidingExpiration = source.UseSlidingExpiration;
                });
            Services.AddScoped<IAuthenticationProvider>(sp =>
            {
                var inner = sp.GetRequiredService<TProvider>();

                // 1. On récupère le moniteur d'options
                var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<CacheOptions>>();

                // 2. On extrait UNIQUEMENT les options liées à ce Scheme (ex: "ApiKey" ou "Jwt")
                var specificCacheOptions = optionsMonitor.Get(schemeName);

                return new CachedAuthenticationProvider(
                    inner,
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<IDistributedCache>(),
                    // 3. L'ASTUCE : On wrappe ces options spécifiques dans un IOptions classique
                    Microsoft.Extensions.Options.Options.Create(specificCacheOptions),
                    sp.GetRequiredService<ILogger<CachedAuthenticationProvider>>()
                );
            });
        }
        else
        {
            Services.AddScoped<IAuthenticationProvider, TProvider>(sp => sp.GetRequiredService<TProvider>());
        }

        return this;
    }

    
}
