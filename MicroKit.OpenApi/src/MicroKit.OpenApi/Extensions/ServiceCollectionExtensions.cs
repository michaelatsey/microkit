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
/// Extension methods for configuring MicroKit OpenAPI services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MicroKit OpenAPI services using configuration from appsettings.json.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The MicroKit OpenAPI builder.</returns>
    public static IMicroKitOpenApiBuilder AddMicroKitOpenApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddMicroKitOpenApi(configuration, _ => { });
    }

    /// <summary>
    /// Adds MicroKit OpenAPI services using configuration from appsettings.json with additional configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configure">Additional configuration action.</param>
    /// <returns>The MicroKit OpenAPI builder.</returns>
    public static IMicroKitOpenApiBuilder AddMicroKitOpenApi(
        this IServiceCollection services,
        IConfiguration? configuration,
        Action<MicroKitOpenApiOptions> configure)
    {
        //ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);

        // Bind configuration section
        // Bind configuration from appsettings.json if available
        if (configuration is not null)
        {
            var section = configuration.GetSection(MicroKitOpenApiDefaults.ConfigurationSectionName);
            services.Configure<MicroKitOpenApiOptions>(section);
        }

        // ApplyAsync code-based configuration
        services.Configure(configure);

        return services.AddMicroKitOpenApiCore(configuration);
    }


    /// <summary>
    /// Adds MicroKit OpenAPI services with code-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns>The MicroKit OpenAPI builder for fluent configuration.</returns>
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
        // Register internal services
        var filterRegistry = new FilterRegistry();
        var scalarOptionsRegistry = new ScalarOptionsRegistry();

        services.TryAddSingleton(filterRegistry);
        services.TryAddSingleton(scalarOptionsRegistry);

        // Register options validator
        services.TryAddSingleton<IValidateOptions<MicroKitOpenApiOptions>, MicroKitOpenApiOptionsValidator>();

        // Configure API versioning using IConfigureOptions pattern
        // This avoids the BuildServiceProvider() anti-pattern
        services.TryAddSingleton<IConfigureOptions<ApiVersioningOptions>, ApiVersioningOptionsConfigurator>();
        services.TryAddSingleton<IConfigureOptions<ApiExplorerOptions>, ApiExplorerOptionsConfigurator>();

        // Register transformers as scoped services for DI
        services.TryAddSingleton<OpenApiDocumentTransformer>();
        services.TryAddSingleton<OpenApiOperationTransformer>();
        services.TryAddSingleton<OpenApiSchemaTransformer>();

        // Post-configure to ensure versions are set correctly
        // Enregistrement du PostConfigurator avec passage de la configuration
        services.TryAddSingleton<IPostConfigureOptions<MicroKitOpenApiOptions>>(sp =>
            new OpenApiPostConfigurator(configuration));

        // Register OpenAPI options configurator using IConfigureNamedOptions
        // This defers transformer registration until options are resolved - NO BuildServiceProvider!
        services.ConfigureOptions<OpenApiOptionsConfigurator>();

        // Register document configurator for runtime access
        services.AddSingleton<OpenApiDocumentsConfigurator>();

        //// Get versions from configuration to register OpenAPI documents
        //// We need to read versions at registration time to call AddOpenApi per version
        var versions = GetVersions(configuration);

        // Register OpenAPI document for each version
        // IMPORTANT : On s'assure que Microsoft OpenAPI est enregistré par version
        foreach (var version in versions)
        {
            services.AddOpenApi($"v{version}");
        }

        // Configure API versioning based on routing style
        ConfigureApiVersioning(services);

        // Create and return builder
        var builder = new MicroKitOpenApiBuilder(services, filterRegistry, scalarOptionsRegistry);
        return builder;
    }

    private static void ConfigureApiVersioning(IServiceCollection services)
    {
        //services.AddEndpointsApiExplorer();
        //services.AddSwaggerGen();

        services.AddApiVersioning(options =>
        {
            // Default options - will be overridden by ApiVersioningOptionsConfigurator
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

            // Extraction manuelle des listes pour éviter le cycle de dépendance des Options
            var supported = section.GetSection("SupportedVersions").Get<List<string>>();
            var deprecated = section.GetSection("DeprecatedVersions").Get<List<string>>();
            var defaultVer = section.GetValue<string>("DefaultVersion");

            if (supported != null) versions.AddRange(supported);
            if (deprecated != null) versions.AddRange(deprecated);
            if (!string.IsNullOrEmpty(defaultVer)) versions.Add(defaultVer);
        }

        // Fallback par défaut si rien n'est configuré
        if (versions.Count == 0)
        {
            versions.Add(MicroKitOpenApiDefaults.DefaultApiVersion); // "1.0"
        }

        return [.. versions.Distinct()];
    }
    
}


/// <summary>
/// Post-configures MicroKitOpenApiOptions to ensure all versions are properly set.
/// </summary>
internal sealed class OpenApiPostConfigurator : IPostConfigureOptions<MicroKitOpenApiOptions>
{
    private readonly IConfiguration? _configuration;

    public OpenApiPostConfigurator(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }
    public void PostConfigure(string? name, MicroKitOpenApiOptions options)
    {
        // 1. Gestion des versions (existant)
        if (!options.SupportedVersions.Contains(options.DefaultVersion) &&
            !options.DeprecatedVersions.Contains(options.DefaultVersion))
        {
            options.SupportedVersions.Insert(0, options.DefaultVersion);
        }

        // 2. Mapping Polymorphique de la Sécurité depuis le JSON
        if (_configuration != null)
        {
            var section = _configuration.GetSection($"{MicroKitOpenApiDefaults.ConfigurationSectionName}:Securities");
            if (section.Exists())
            {
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

                    // On ajoute seulement si le SchemeName n'est pas déjà présent (évite les doublons code/json)
                    if (
                        scheme != null && 
                        options.Securities is not null && 
                        !options.Securities.Any(s => s.SchemeName == scheme.SchemeName))
                    {
                        if (string.IsNullOrWhiteSpace(scheme.Description))
                        {
                            scheme.Description = scheme.SchemeName switch
                            {
                                "Tenant" => "Identifiant de contexte organisationnel (Multi-tenant).",
                                "Bearer" => "Authentification basée sur un jeton JWT.",
                                _ => "Sécurité requise pour l'accès aux ressources."
                            };
                        }
                        options.Securities.Add(scheme);
                    }
                }
            }
        }
    }
}


/// <summary>
/// Provides access to configured versions at runtime.
/// </summary>
internal sealed class OpenApiDocumentsConfigurator
{
    private readonly IOptions<MicroKitOpenApiOptions> _options;

    public OpenApiDocumentsConfigurator(IOptions<MicroKitOpenApiOptions> options)
    {
        _options = options;
    }

    public IEnumerable<string> GetAllVersions()
    {
        var opts = _options.Value;
        return opts.SupportedVersions
                .Concat(opts.DeprecatedVersions)
                .Distinct()
                .OrderByDescending(v => v);
    }
}
