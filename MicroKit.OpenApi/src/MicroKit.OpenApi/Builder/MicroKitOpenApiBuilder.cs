using MicroKit.OpenApi.Abstractions;
using MicroKit.OpenApi.Filters;
using MicroKit.OpenApi.Internal;
using MicroKit.OpenApi.Options;

namespace MicroKit.OpenApi.Builder;

/// <summary>
/// Fluent builder for configuring MicroKit OpenAPI services.
/// </summary>
internal sealed class MicroKitOpenApiBuilder : IMicroKitOpenApiBuilder
{
    private readonly FilterRegistry _filterRegistry;
    private readonly ScalarOptionsRegistry _scalarOptionsRegistry;

    internal MicroKitOpenApiBuilder(
        IServiceCollection services,
        FilterRegistry filterRegistry,
        ScalarOptionsRegistry scalarOptionsRegistry)
    {
        Services = services;
        _filterRegistry = filterRegistry;
        _scalarOptionsRegistry = scalarOptionsRegistry;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder Configure(Action<MicroKitOpenApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddBearerSecurity(Action<BearerSecurityOptions>? configure = null)
    {
        return AddSecurity(configure);
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddOAuth2Security(Action<OAuth2SecurityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddSecurity(configure);
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddApiKeySecurity(Action<ApiKeySecurityOptions>? configure = null)
    {
        return AddSecurity(configure);
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddDocumentFilter<TFilter>() where TFilter : class, IOpenApiDocumentFilter
    {
        Services.AddTransient<TFilter>();
        _filterRegistry.AddDocumentFilter<TFilter>();
        return this;
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddOperationFilter<TFilter>() where TFilter : class, IOpenApiOperationFilter
    {
        Services.AddTransient<TFilter>();
        _filterRegistry.AddOperationFilter<TFilter>();
        return this;
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddSchemaFilter<TFilter>() where TFilter : class, IOpenApiSchemaFilter
    {
        Services.AddTransient<TFilter>();
        _filterRegistry.AddSchemaFilter<TFilter>();
        return this;
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddVersion(string version, bool deprecated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Services.AddOpenApi($"v{version}");

        Services.Configure<MicroKitOpenApiOptions>(options =>
        {
            if (deprecated)
            {
                if (!options.DeprecatedVersions.Contains(version))
                    options.DeprecatedVersions.Add(version);
                options.SupportedVersions.Remove(version);
            }
            else
            {
                if (!options.SupportedVersions.Contains(version))
                    options.SupportedVersions.Add(version);
                options.DeprecatedVersions.Remove(version);
            }
        });

        return this;
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder AddServer(string url, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Services.Configure<MicroKitOpenApiOptions>(options =>
            options.Servers.Add(new ServerOptions { Url = url, Description = description }));

        return this;
    }

    /// <inheritdoc />
    public IMicroKitOpenApiBuilder ConfigureScalar(Action<Abstractions.ScalarOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _scalarOptionsRegistry.Configure(configure);
        return this;
    }

    private IMicroKitOpenApiBuilder AddSecurity<T>(Action<T>? configure) where T : SecuritySchemeOptions, new()
    {
        Services.Configure<MicroKitOpenApiOptions>(options =>
        {
            var security = new T();
            configure?.Invoke(security);

            if (options.Securities is not null &&
                !options.Securities.Any(s => s.SchemeName.Equals(security.SchemeName, StringComparison.OrdinalIgnoreCase)))
            {
                options.Securities.Add(security);
            }
        });

        return this;
    }
}
