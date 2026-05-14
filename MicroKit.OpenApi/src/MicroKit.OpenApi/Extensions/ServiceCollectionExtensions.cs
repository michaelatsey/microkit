using MicroKit.OpenApi.Abstractions;
using MicroKit.OpenApi.Builder;
using MicroKit.OpenApi.Configuration;
using MicroKit.OpenApi.Constants;
using MicroKit.OpenApi.Internal;
using MicroKit.OpenApi.Options;
using MicroKit.OpenApi.Validator;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.OpenApi.Extensions;

/// <summary>
/// Extension methods for adding MicroKit OpenAPI services to an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MicroKit OpenAPI services, reading configuration from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration (reads <c>MicroKit:OpenApi</c> section).</param>
    /// <returns>The builder for fluent configuration.</returns>
    public static IMicroKitOpenApiBuilder AddMicroKitOpenApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddMicroKitOpenApi(configuration, _ => { });
    }

    /// <summary>
    /// Adds MicroKit OpenAPI services with configuration from <paramref name="configuration"/> and an additional code-based override.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configure">Code-based override applied on top of the configuration file values.</param>
    /// <returns>The builder for fluent configuration.</returns>
    public static IMicroKitOpenApiBuilder AddMicroKitOpenApi(
        this IServiceCollection services,
        IConfiguration? configuration,
        Action<MicroKitOpenApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (configuration is not null)
        {
            services.Configure<MicroKitOpenApiOptions>(
                configuration.GetSection(MicroKitOpenApiDefaults.ConfigurationSectionName));
        }

        services.Configure(configure);

        return services.AddMicroKitOpenApiCore(configuration);
    }

    /// <summary>
    /// Adds MicroKit OpenAPI services with code-based configuration only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for <see cref="MicroKitOpenApiOptions"/>.</param>
    /// <returns>The builder for fluent configuration.</returns>
    public static IMicroKitOpenApiBuilder AddMicroKitOpenApi(
        this IServiceCollection services,
        Action<MicroKitOpenApiOptions> configure)
    {
        return services.AddMicroKitOpenApi(null, configure);
    }

    private static IMicroKitOpenApiBuilder AddMicroKitOpenApiCore(
        this IServiceCollection services,
        IConfiguration? configuration)
    {
        var filterRegistry = new FilterRegistry();
        var scalarOptionsRegistry = new ScalarOptionsRegistry();

        services.TryAddSingleton(filterRegistry);
        services.TryAddSingleton(scalarOptionsRegistry);

        services.TryAddSingleton<IValidateOptions<MicroKitOpenApiOptions>, MicroKitOpenApiOptionsValidator>();

        services.TryAddSingleton<IConfigureOptions<ApiVersioningOptions>, ApiVersioningOptionsConfigurator>();
        services.TryAddSingleton<IConfigureOptions<ApiExplorerOptions>, ApiExplorerOptionsConfigurator>();

        services.TryAddSingleton<OpenApiDocumentTransformer>();
        services.TryAddSingleton<OpenApiOperationTransformer>();
        services.TryAddSingleton<OpenApiSchemaTransformer>();

        services.TryAddSingleton<IPostConfigureOptions<MicroKitOpenApiOptions>>(
            _ => new OpenApiPostConfigurator(configuration));

        services.ConfigureOptions<OpenApiOptionsConfigurator>();

        services.AddSingleton<OpenApiDocumentsConfigurator>();

        var versions = GetVersions(configuration);
        foreach (var version in versions)
        {
            services.AddOpenApi($"v{version}");
        }

        ConfigureApiVersioning(services);

        return new MicroKitOpenApiBuilder(services, filterRegistry, scalarOptionsRegistry);
    }

    private static void ConfigureApiVersioning(IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.ReportApiVersions = true;
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ApiVersionReader = ApiVersionReader.Combine(
                new UrlSegmentApiVersionReader(),
                new HeaderApiVersionReader("X-Api-Version"),
                new QueryStringApiVersionReader("api-version")
            );
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });
    }

    private static List<string> GetVersions(IConfiguration? configuration)
    {
        var versions = new List<string>();

        if (configuration is not null)
        {
            var section = configuration.GetSection(MicroKitOpenApiDefaults.ConfigurationSectionName);
            var supported = section.GetSection("SupportedVersions").Get<List<string>>();
            var deprecated = section.GetSection("DeprecatedVersions").Get<List<string>>();
            var defaultVer = section.GetValue<string>("DefaultVersion");

            if (supported != null) versions.AddRange(supported);
            if (deprecated != null) versions.AddRange(deprecated);
            if (!string.IsNullOrEmpty(defaultVer)) versions.Add(defaultVer);
        }

        if (versions.Count == 0)
        {
            versions.Add(MicroKitOpenApiDefaults.DefaultApiVersion);
        }

        return [.. versions.Distinct()];
    }
}

/// <summary>
/// Post-configures <see cref="MicroKitOpenApiOptions"/> to ensure the default version is always present
/// and to map polymorphic security schemes from JSON configuration.
/// </summary>
internal sealed class OpenApiPostConfigurator : IPostConfigureOptions<MicroKitOpenApiOptions>
{
    private readonly IConfiguration? _configuration;

    /// <summary>Initializes a new instance.</summary>
    public OpenApiPostConfigurator(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, MicroKitOpenApiOptions options)
    {
        if (!options.SupportedVersions.Contains(options.DefaultVersion) &&
            !options.DeprecatedVersions.Contains(options.DefaultVersion))
        {
            options.SupportedVersions.Insert(0, options.DefaultVersion);
        }

        if (_configuration is null)
        {
            return;
        }

        // JSON security schemes use a "Type" discriminator for polymorphic deserialization.
        var section = _configuration.GetSection($"{MicroKitOpenApiDefaults.ConfigurationSectionName}:Securities");
        if (!section.Exists())
        {
            return;
        }

        foreach (var subSection in section.GetChildren())
        {
            var type = subSection.GetValue<string>("Type");

            SecuritySchemeOptions? scheme = type?.ToLowerInvariant() switch
            {
                "bearer" => subSection.Get<BearerSecurityOptions>(),
                "apikey" => subSection.Get<ApiKeySecurityOptions>(),
                "oauth2" => subSection.Get<OAuth2SecurityOptions>(),
                _ => null
            };

            if (scheme is null || options.Securities is null)
            {
                continue;
            }

            if (!options.Securities.Any(s => s.SchemeName == scheme.SchemeName))
            {
                options.Securities.Add(scheme);
            }
        }
    }
}

/// <summary>
/// Provides the list of all configured API version document names at runtime.
/// </summary>
internal sealed class OpenApiDocumentsConfigurator
{
    private readonly IOptions<MicroKitOpenApiOptions> _options;

    /// <summary>Initializes a new instance.</summary>
    public OpenApiDocumentsConfigurator(IOptions<MicroKitOpenApiOptions> options)
    {
        _options = options;
    }

    /// <summary>Returns all version strings (supported + deprecated), ordered descending.</summary>
    public IEnumerable<string> GetAllVersions()
    {
        var opts = _options.Value;
        return opts.SupportedVersions
            .Concat(opts.DeprecatedVersions)
            .Distinct()
            .OrderByDescending(v => v);
    }
}
