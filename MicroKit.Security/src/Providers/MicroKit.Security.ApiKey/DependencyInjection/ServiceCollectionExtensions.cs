
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

/// <summary>Extension methods for registering API key authentication.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds API key authentication using the in-memory store.</summary>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">Application configuration used to bind <see cref="ApiKeyOptions"/>.</param>
    /// <param name="configure">Optional additional configuration delegate.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddApiKey(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? configure = null)
    {
        AddApiKeyOptions(builder, configuration, configure);
        return AddCoreServices<InMemoryApiKeyStore>(builder);
    }

    /// <summary>Adds API key authentication using a custom store implementation.</summary>
    /// <typeparam name="TStore">The <see cref="IApiKeyStore"/> implementation to register.</typeparam>
    /// <param name="builder">The security builder.</param>
    /// <param name="configuration">Application configuration used to bind <see cref="ApiKeyOptions"/>.</param>
    /// <param name="configure">Optional additional configuration delegate.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static SecurityBuilder AddApiKey<TStore>(
        this SecurityBuilder builder,
        IConfiguration configuration,
        Action<ApiKeyOptions>? configure = null)
        where TStore : class, IApiKeyStore
    {
        AddApiKeyOptions(builder, configuration, configure);
        return AddCoreServices<TStore>(builder);
    }

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
            optionsBuilder.Configure(configure);
    }

    private static SecurityBuilder AddCoreServices<TStore>(SecurityBuilder builder)
        where TStore : class, IApiKeyStore
    {
        builder.Services.TryAddSingleton<IApiKeyValidator, DefaultApiKeyValidator>();
        builder.Services.TryAddSingleton<IApiKeyStore, TStore>();
        builder.Services.TryAddScoped<IApiKeyService, ApiKeyService>();
        return builder;
    }
}