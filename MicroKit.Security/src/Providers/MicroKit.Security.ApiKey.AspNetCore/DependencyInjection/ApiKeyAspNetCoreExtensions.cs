using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using MicroKit.Security.Abstractions.Validator;
using MicroKit.Security.ApiKey.AspNetCore.Extraction;
using MicroKit.Security.ApiKey.DependencyInjection;
using MicroKit.Security.ApiKey.Options;
using MicroKit.Security.ApiKey.Providers;
using MicroKit.Security.ApiKey.Stores;
using MicroKit.Security.ApiKey.Validation;
using MicroKit.Security.AspNetCore.Extraction;
using MicroKit.Security.Core.Builder;
using MicroKit.Security.Core.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
namespace MicroKit.Security.ApiKey.AspNetCore.DependencyInjection;

public static class ApiKeyAspNetCoreExtensions
{
    public static SecurityBuilder AddApiKeyAspNetCore(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? optionsConfigure = null,
        bool useCache = false)
    {
        // 1. Appel au code métier existant (Projet .ApiKey)
        // Cela enregistre ApiKeyOptions, IApiKeyService, IApiKeyStore, etc.
        builder.AddApiKey(configuration, optionsConfigure);

        builder.Services.AddSingleton<IAuthenticationExtractor, ApiKeyExtractor>();

        // 3. NOUVEAUTÉ 2026 : Enregistre le Validateur de Contexte
        // Cela permet au AuthenticationService de valider l'API Key par rapport au JWT
        builder.Services.AddSingleton<ISecurityValidator, ApiKeyContextValidator>();

        // 3.NOUVEAUTÉ 2026 : Enregistre le Validateur de Contexte
        // Cela permet au AuthenticationService de valider l'API Key par rapport au JWT
        builder.Services.AddSingleton<ISecurityValidator, ApiKeyContextValidator>();

        var scheme = AuthenticationScheme.ApiKey.ToString();

        return builder.AddProvider<ApiKeyAuthenticationProvider, ApiKeyOptions>(scheme,enableCache: useCache);
    }

    public static SecurityBuilder AddApiKeyAspNetCore<TStore>(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? optionsConfigure = null,
        bool useCache = false)
        where TStore : class, IApiKeyStore

    {
        // 1. Appel au code métier existant (Projet .ApiKey)
        // Cela enregistre ApiKeyOptions, IApiKeyService, IApiKeyStore, etc.
        builder.AddApiKey<TStore>(configuration, optionsConfigure);

        builder.Services.AddSingleton<IAuthenticationExtractor, ApiKeyExtractor>();

        // 3. NOUVEAUTÉ 2026 : Enregistre le Validateur de Contexte
        // Cela permet au AuthenticationService de valider l'API Key par rapport au JWT
        builder.Services.AddSingleton<ISecurityValidator, ApiKeyContextValidator>();

        var scheme = AuthenticationScheme.ApiKey.ToString();

        return builder.AddProvider<ApiKeyAuthenticationProvider, ApiKeyOptions>(scheme, enableCache: useCache);
    }

}
