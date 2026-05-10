
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.ApiKey.Options;
using MicroKit.Security.ApiKey.Services;
using MicroKit.Security.ApiKey.Stores;
using MicroKit.Security.ApiKey.Validation;
using MicroKit.Security.Core.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.Security.ApiKey.DependencyInjection;
/// <summary>
/// Méthodes d'extension pour l'enregistrement de l'authentification par ApiKey.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Ajoute l'authentification ApiKey avec configuration par délégué.
    /// </summary>
    public static SecurityBuilder AddApiKey(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? configure = null)
    {
        AddApiKeyOptions(builder, configuration, configure);
        return AddCoreServices<InMemoryApiKeyStore>(builder);
    }

    /// <summary>
    /// Ajoute l'authentification ApiKey avec un Store personnalisé.
    /// </summary>
    public static SecurityBuilder AddApiKey<TStore>(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? configure = null)
        where TStore : class, IApiKeyStore
    {
        AddApiKeyOptions(builder, configuration, configure);
        return AddCoreServices<TStore>(builder);
    }


    /// <summary>
    /// Adds the API key options.
    /// Méthodes privées pour centraliser la logique
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configuration"></param>
    /// <param name="configure">The configure.</param>
    private static void AddApiKeyOptions(
        SecurityBuilder builder, 
        IConfiguration configuration, 
        Action<ApiKeyOptions>? configure = null)
    {
        var optionsBuilder = builder.Services
            .AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection(ApiKeyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
    }

    /// <summary>
    /// Adds the core services.
    /// </summary>
    /// <typeparam name="TStore">The type of the store.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns></returns>
    private static SecurityBuilder AddCoreServices<TStore>(SecurityBuilder builder)
        where TStore : class, IApiKeyStore
    {
        // On le met en Singleton s'il ne dépend pas d'un DbContext "Scoped"
        builder.Services.TryAddSingleton<IApiKeyValidator, DefaultApiKeyValidator>();

        // On évite les doublons si la méthode est appelée plusieurs fois
        builder.Services.TryAddSingleton<IApiKeyStore, TStore>();
        builder.Services.TryAddScoped<IApiKeyService, ApiKeyService>();
        return builder;
    }
}