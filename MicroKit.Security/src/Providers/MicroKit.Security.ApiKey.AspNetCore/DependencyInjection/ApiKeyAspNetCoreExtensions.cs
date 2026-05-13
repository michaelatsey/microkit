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

/// <summary>Extension methods for registering API key authentication with ASP.NET Core.</summary>
public static class ApiKeyAspNetCoreExtensions
{
    /// <summary>Registers API key authentication using the default in-memory store.</summary>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">The application configuration used to bind API key options.</param>
    /// <param name="optionsConfigure">Optional callback to configure <see cref="ApiKeyOptions"/>.</param>
    /// <param name="useCache">When <see langword="true"/>, enables caching for authentication results.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddApiKeyAspNetCore(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? optionsConfigure = null,
        bool useCache = false)
    {
        builder.AddApiKey(configuration, optionsConfigure);

        builder.Services.AddSingleton<IAuthenticationExtractor, ApiKeyExtractor>();
        builder.Services.AddSingleton<ISecurityValidator, ApiKeyContextValidator>();

        var scheme = AuthenticationScheme.ApiKey.ToString();

        return builder.AddProvider<ApiKeyAuthenticationProvider, ApiKeyOptions>(scheme,enableCache: useCache);
    }

    /// <summary>Registers API key authentication using a custom <typeparamref name="TStore"/> implementation.</summary>
    /// <typeparam name="TStore">The custom <see cref="IApiKeyStore"/> implementation.</typeparam>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">The application configuration used to bind API key options.</param>
    /// <param name="optionsConfigure">Optional callback to configure <see cref="ApiKeyOptions"/>.</param>
    /// <param name="useCache">When <see langword="true"/>, enables caching for authentication results.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddApiKeyAspNetCore<TStore>(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? optionsConfigure = null,
        bool useCache = false)
        where TStore : class, IApiKeyStore

    {
        builder.AddApiKey<TStore>(configuration, optionsConfigure);

        builder.Services.AddSingleton<IAuthenticationExtractor, ApiKeyExtractor>();
        builder.Services.AddSingleton<ISecurityValidator, ApiKeyContextValidator>();

        var scheme = AuthenticationScheme.ApiKey.ToString();

        return builder.AddProvider<ApiKeyAuthenticationProvider, ApiKeyOptions>(scheme, enableCache: useCache);
    }

}
