using MicroKit.OpenApi.Internal;
using MicroKit.OpenApi.Options;
using ScalarTheme = MicroKit.OpenApi.Options.ScalarTheme;

namespace MicroKit.OpenApi.Extensions;

/// <summary>
/// Extension methods for configuring MicroKit OpenAPI middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers OpenAPI documents for each configured version.
    /// Must be called during service configuration, before <c>Build()</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The MicroKit OpenAPI options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMicroKitOpenApiDocuments(
        this IServiceCollection services,
        MicroKitOpenApiOptions options)
    {
        var allVersions = options.SupportedVersions
            .Concat(options.DeprecatedVersions)
            .Distinct()
            .OrderByDescending(v => v)
            .ToList();

        foreach (var version in allVersions)
        {
            services.AddOpenApi($"v{version}");
        }

        return services;
    }

    /// <summary>
    /// Maps MicroKit OpenAPI endpoints including the OpenAPI JSON documents and Scalar UI.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder UseMicroKitOpenApi(this IEndpointRouteBuilder app)
    {
        var services = app.ServiceProvider;
        var options = services.GetRequiredService<IOptions<MicroKitOpenApiOptions>>().Value;
        var configurator = services.GetRequiredService<OpenApiDocumentsConfigurator>();
        var scalarRegistry = services.GetRequiredService<ScalarOptionsRegistry>();
        var scalarOptions = scalarRegistry.Options;

        var allVersions = configurator.GetAllVersions().ToList();

        app.MapOpenApi();

        if (!options.EnableScalar)
        {
            return app;
        }

        var endpointPrefix = options.ScalarEndpointPath.Replace("/{documentName}", "");
        if (string.IsNullOrWhiteSpace(endpointPrefix))
        {
            endpointPrefix = "/scalar";
        }

        app.MapScalarApiReference(endpointPrefix, scalarConfig =>
        {
            scalarConfig.Title = options.Title;
            scalarConfig.Theme = MapTheme(
                scalarOptions.Theme != ScalarTheme.Default ? scalarOptions.Theme : options.Theme);
            scalarConfig.DarkMode = scalarOptions.DarkMode;
            scalarConfig.ShowSidebar = scalarOptions.ShowSidebar;

            if (!string.IsNullOrWhiteSpace(scalarOptions.Favicon))
            {
                scalarConfig.Favicon = scalarOptions.Favicon;
            }

            if (!string.IsNullOrWhiteSpace(scalarOptions.CustomCss))
            {
                scalarConfig.CustomCss = scalarOptions.CustomCss;
            }

            if (scalarOptions.ShowDownloadButton)
            {
                scalarConfig.WithDocumentDownloadType(DocumentDownloadType.Both);
            }

            var defaultVersion = options.DefaultVersion;
            foreach (var version in allVersions)
            {
                var documentName = $"v{version}";
                var isDeprecated = options.DeprecatedVersions.Contains(version);
                var title = isDeprecated ? $"API {documentName} (Deprecated)" : $"API {documentName}";
                var documentUrl = $"/openapi/{documentName}.json";

                scalarConfig.AddDocument(
                    documentName,
                    title,
                    routePattern: documentUrl,
                    isDefault: version == defaultVersion);
            }

            ApplySecurityConfiguration(scalarConfig, options);
        })
        .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Maps MicroKit OpenAPI endpoints for <see cref="WebApplication"/>.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application.</returns>
    public static WebApplication UseMicroKitOpenApi(this WebApplication app)
    {
        ((IEndpointRouteBuilder)app).UseMicroKitOpenApi();
        return app;
    }

    /// <summary>
    /// Maps MicroKit OpenAPI endpoints with additional Scalar UI configuration.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="configureScalar">Additional Scalar UI configuration applied after defaults.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseMicroKitOpenApi(
        this WebApplication app,
        Action<MicroKit.OpenApi.Abstractions.ScalarOptions> configureScalar)
    {
        var scalarRegistry = app.Services.GetRequiredService<ScalarOptionsRegistry>();
        scalarRegistry.Configure(configureScalar);
        return app.UseMicroKitOpenApi();
    }

    private static void ApplySecurityConfiguration(ScalarOptions scalarOptions, MicroKitOpenApiOptions microKitOptions)
    {
        foreach (var security in microKitOptions.Securities ?? [])
        {
            switch (security)
            {
                case BearerSecurityOptions bearer:
                    scalarOptions.AddPreferredSecuritySchemes(bearer.SchemeName);
                    if (!string.IsNullOrEmpty(bearer.PrefilledValue))
                    {
                        scalarOptions.AddHttpAuthentication(bearer.SchemeName, auth =>
                        {
                            auth.Token = bearer.PrefilledValue;
                        });
                    }
                    break;

                case ApiKeySecurityOptions apiKey:
                    scalarOptions.AddPreferredSecuritySchemes(apiKey.SchemeName);
                    if (!string.IsNullOrEmpty(apiKey.PrefilledValue))
                    {
                        scalarOptions.AddApiKeyAuthentication(apiKey.SchemeName, auth =>
                        {
                            auth.Value = apiKey.PrefilledValue;
                        });
                    }
                    break;

                case OAuth2SecurityOptions oauth:
                    scalarOptions.AddPreferredSecuritySchemes(oauth.SchemeName);
                    ApplyOAuth2Flow(scalarOptions, oauth);
                    break;
            }
        }
    }

    private static void ApplyOAuth2Flow(ScalarOptions options, OAuth2SecurityOptions oauth)
    {
        switch (oauth.FlowType)
        {
            case OAuth2FlowType.ClientCredentials:
                options.AddClientCredentialsFlow(oauth.SchemeName, flow =>
                {
                    flow.ClientId = oauth.PrefilledClientId;
                    flow.ClientSecret = oauth.PrefilledClientSecret;
                    if (oauth.PreselectedScopes.Count > 0)
                    {
                        flow.SelectedScopes = oauth.PreselectedScopes;
                    }
                });
                break;

            case OAuth2FlowType.AuthorizationCode:
                options.AddAuthorizationCodeFlow(oauth.SchemeName, flow =>
                {
                    flow.ClientId = oauth.PrefilledClientId;
                    flow.ClientSecret = oauth.PrefilledClientSecret;
                    if (oauth.EnablePkce)
                    {
                        flow.Pkce = Pkce.Sha256;
                    }
                    if (oauth.PreselectedScopes.Count > 0)
                    {
                        flow.SelectedScopes = oauth.PreselectedScopes;
                    }
                });
                break;

            case OAuth2FlowType.Password:
                options.AddPasswordFlow(oauth.SchemeName, flow =>
                {
                    flow.ClientId = oauth.PrefilledClientId;
                    flow.Username = oauth.PrefilledUsername;
                    flow.Password = oauth.PrefilledPassword;
                    if (oauth.PreselectedScopes.Count > 0)
                    {
                        flow.SelectedScopes = oauth.PreselectedScopes;
                    }
                });
                break;

            case OAuth2FlowType.Implicit:
                options.AddImplicitFlow(oauth.SchemeName, flow =>
                {
                    flow.ClientId = oauth.PrefilledClientId;
                    if (oauth.PreselectedScopes.Count > 0)
                    {
                        flow.SelectedScopes = oauth.PreselectedScopes;
                    }
                });
                break;
        }
    }

    private static global::Scalar.AspNetCore.ScalarTheme MapTheme(ScalarTheme theme) => theme switch
    {
        ScalarTheme.Default => global::Scalar.AspNetCore.ScalarTheme.Default,
        ScalarTheme.Alternate => global::Scalar.AspNetCore.ScalarTheme.Alternate,
        ScalarTheme.Moon => global::Scalar.AspNetCore.ScalarTheme.Moon,
        ScalarTheme.Purple => global::Scalar.AspNetCore.ScalarTheme.Purple,
        ScalarTheme.Solarized => global::Scalar.AspNetCore.ScalarTheme.Solarized,
        ScalarTheme.BluePlanet => global::Scalar.AspNetCore.ScalarTheme.BluePlanet,
        ScalarTheme.Saturn => global::Scalar.AspNetCore.ScalarTheme.Saturn,
        ScalarTheme.Kepler => global::Scalar.AspNetCore.ScalarTheme.Kepler,
        ScalarTheme.Mars => global::Scalar.AspNetCore.ScalarTheme.Mars,
        ScalarTheme.DeepSpace => global::Scalar.AspNetCore.ScalarTheme.DeepSpace,
        _ => global::Scalar.AspNetCore.ScalarTheme.Default
    };
}
