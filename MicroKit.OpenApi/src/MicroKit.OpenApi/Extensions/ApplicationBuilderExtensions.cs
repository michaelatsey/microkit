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
    /// Adds MicroKit OpenAPI services to the service collection for each configured version.
    /// This must be called during service configuration, before Build().
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
            var documentName = $"v{version}";
            services.AddOpenApi(documentName, openApiOptions =>
            {
                // Transformers will be added by OpenApiDocumentsConfigurator
            });
        }

        return services;
    }

    /// <summary>
    /// Maps MicroKit OpenAPI endpoints including Scalar UI.
    /// </summary>
    /// <param name="app">The endpoint route builder (WebApplication).</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder UseMicroKitOpenApi(this IEndpointRouteBuilder app)
    {
        var services = app.ServiceProvider;
        var options = services.GetRequiredService<IOptions<MicroKitOpenApiOptions>>().Value;
        var configurator = services.GetRequiredService<OpenApiDocumentsConfigurator>();
        var scalarRegistry = services.GetRequiredService<ScalarOptionsRegistry>();
        var scalarOptions = scalarRegistry.Options;

        // Get all versions (supported + deprecated)
        var allVersions = configurator.GetAllVersions().ToList();

        app.MapOpenApi();
        // Configure Scalar UI if enabled
        if (options.EnableScalar)
        {
            // Get the endpoint prefix from options (e.g., "/scalar/{documentName}" -> "/scalar")
            var endpointPrefix = options.ScalarEndpointPath.Replace("/{documentName}", "");
            if (string.IsNullOrWhiteSpace(endpointPrefix))
            {
                endpointPrefix = "/scalar";
            }
            //app.MapSwagger("/openapi/{documentName}.json");

            // Apply security pre-fill configuration
            //ApplySecurityConfiguration(scalarRegistry.Options, options.Securities);

            // MapScalarApiReference with endpointPrefix parameter (Scalar.AspNetCore 2.0+)
            app.MapScalarApiReference(endpointPrefix, scalarConfig =>
            {
                scalarConfig.Title = options.Title;
                scalarConfig.Theme = MapTheme(scalarOptions.Theme != ScalarTheme.Default ? scalarOptions.Theme : options.Theme);
                scalarConfig.DarkMode = scalarOptions.DarkMode;

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
                scalarConfig.ShowSidebar = scalarOptions.ShowSidebar;

                // Set default document
                //if (allVersions.Count > 0)
                //{
                //    scalarConfig.OpenApiRoutePattern = $"/openapi/v{options.DefaultVersion}.json";
                //}

                // Add all API versions as documents with version selector
                // This enables the dropdown in Scalar UI to switch between versions
                var defaultVersion = options.DefaultVersion;

                foreach (var version in allVersions)
                {
                    //var documentName = version.Contains('.') ? $"v{version.Split('.')[0]}" : $"v{version}";
                    var documentName = $"v{version}";
                    var isDefault = version == defaultVersion;
                    var isDeprecated = options.DeprecatedVersions.Contains(version);
                    var title = isDeprecated
                        ? $"API {documentName} (Deprecated)"
                        : $"API {documentName}";

                    // OpenAPI document URL pattern: /openapi/{documentName}.json
                    var documentUrl = $"/openapi/{documentName}.json";

                    scalarConfig.AddDocument(
                        documentName,
                        title,
                        routePattern: documentUrl,
                        isDefault: isDefault);
                }
                // 3. Appel de la méthode de configuration de sécurité
                ApplySecurityConfiguration(scalarConfig, options);

            })
                .AllowAnonymous();
        }

        return app;
    }

    /// <summary>
    /// Maps MicroKit OpenAPI endpoints for WebApplication.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application.</returns>
    public static WebApplication UseMicroKitOpenApi(this WebApplication app)
    {
        ((IEndpointRouteBuilder)app).UseMicroKitOpenApi();
        return app;
    }

    /// <summary>
    /// Adds MicroKit OpenAPI endpoints with custom Scalar configuration.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="configureScalar">Additional Scalar configuration.</param>
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
        var preferredSchemes = new List<string>();

        foreach (var security in microKitOptions.Securities)
        {
            switch (security)
            {
                case BearerSecurityOptions bearer:
                    preferredSchemes.Add(bearer.SchemeName);
                    if (!string.IsNullOrEmpty(bearer.PrefilledValue))
                    {
                        // Nouvelle API pour le Bearer (HTTP Auth)
                        scalarOptions.AddPreferredSecuritySchemes(bearer.SchemeName);
                        scalarOptions.AddHttpAuthentication(bearer.SchemeName, auth =>
                        {
                            auth.Token = bearer.PrefilledValue;
                        });
                    }
                    break;

                case ApiKeySecurityOptions apiKey:
                    preferredSchemes.Add(apiKey.SchemeName);
                    if (!string.IsNullOrEmpty(apiKey.PrefilledValue))
                    {
                        // Nouvelle API pour l'API Key
                        scalarOptions.AddApiKeyAuthentication(apiKey.SchemeName, auth =>
                        {
                            auth.Value = apiKey.PrefilledValue;
                        });
                    }
                    break;
                
                case OAuth2SecurityOptions oauth:
                    preferredSchemes.Add(oauth.SchemeName);
                    ApplyOAuth2Flow(scalarOptions, oauth);
                    break;
            }
        }

        // Définit les schémas sélectionnés par défaut dans l'UI
        //if (preferredSchemes.Count > 0)
        //{
        //    scalarOptions.AddPreferredSecuritySchemes([.. preferredSchemes]);
        //}
    }

    private static void ApplyOAuth2Flow(ScalarOptions options, OAuth2SecurityOptions oauth)
    {
        // Exemple pour Client Credentials
        if (oauth.FlowType == OAuth2FlowType.ClientCredentials)
        {
            options.AddClientCredentialsFlow(oauth.SchemeName, flow =>
            {
                flow.ClientId = oauth.PrefilledClientId;
                flow.ClientSecret = oauth.PrefilledClientSecret;
                if (oauth.PreselectedScopes.Count != 0 == true)
                    flow.SelectedScopes = oauth.PreselectedScopes;
            });
        }
        // ... Répéter pour AuthorizationCode, Password, etc.
    }
    /// <summary>
    /// Applies security configuration to Scalar options using the new Scalar authentication API.
    /// </summary>
    //private static void ApplySecurityConfiguration(ScalarOptions scalarOptions, SecurityOptions security)
    //{
    //    if (security is null || security.Type == SecurityType.None)
    //    {
    //        return;
    //    }

    //    // Collect preferred schemes
    //    var preferredSchemes = security.PreferredSchemes.ToList();

    //    // Configure JWT Bearer authentication
    //    if (security.EnableJwtBearer && security.JwtBearer is not null)
    //    {
    //        var jwt = security.JwtBearer;
    //        if (!preferredSchemes.Contains(jwt.SchemeName))
    //        {
    //            preferredSchemes.Add(jwt.SchemeName);
    //        }

    //        if (!string.IsNullOrEmpty(jwt.PrefilledToken))
    //        {
    //            scalarOptions.AddHttpAuthentication(jwt.SchemeName, auth =>
    //            {
    //                auth.Token = jwt.PrefilledToken;
    //            });
    //        }
    //    }

    //    // Configure API Key authentication
    //    if (security.EnableApiKey && security.ApiKey is not null)
    //    {
    //        var apiKey = security.ApiKey;
    //        if (!preferredSchemes.Contains(apiKey.SchemeName))
    //        {
    //            preferredSchemes.Add(apiKey.SchemeName);
    //        }

    //        if (!string.IsNullOrEmpty(apiKey.PrefilledValue))
    //        {
    //            scalarOptions.AddApiKeyAuthentication(apiKey.SchemeName, auth =>
    //            {
    //                auth.Value = apiKey.PrefilledValue;
    //            });
    //        }
    //    }

    //    // Configure OAuth2 authentication
    //    if (security.EnableOAuth2 && security.OAuth2 is not null)
    //    {
    //        var oauth = security.OAuth2;
    //        if (!preferredSchemes.Contains(oauth.SchemeName))
    //        {
    //            preferredSchemes.Add(oauth.SchemeName);
    //        }

    //        switch (oauth.FlowType)
    //        {
    //            case OAuth2FlowType.AuthorizationCode:
    //                scalarOptions.AddAuthorizationCodeFlow(oauth.SchemeName, flow =>
    //                {
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledClientId))
    //                        flow.ClientId = oauth.PrefilledClientId;
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledClientSecret))
    //                        flow.ClientSecret = oauth.PrefilledClientSecret;
    //                    if (oauth.EnablePkce)
    //                        flow.Pkce = Pkce.Sha256;
    //                    if (oauth.PreselectedScopes.Count > 0)
    //                        flow.SelectedScopes = oauth.PreselectedScopes;
    //                });
    //                break;

    //            case OAuth2FlowType.ClientCredentials:
    //                scalarOptions.AddClientCredentialsFlow(oauth.SchemeName, flow =>
    //                {
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledClientId))
    //                        flow.ClientId = oauth.PrefilledClientId;
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledClientSecret))
    //                        flow.ClientSecret = oauth.PrefilledClientSecret;
    //                    if (oauth.PreselectedScopes.Count > 0)
    //                        flow.SelectedScopes = oauth.PreselectedScopes;
    //                });
    //                break;

    //            case OAuth2FlowType.Password:
    //                scalarOptions.AddPasswordFlow(oauth.SchemeName, flow =>
    //                {
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledClientId))
    //                        flow.ClientId = oauth.PrefilledClientId;
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledUsername))
    //                        flow.Username = oauth.PrefilledUsername;
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledPassword))
    //                        flow.Password = oauth.PrefilledPassword;
    //                    if (oauth.PreselectedScopes.Count > 0)
    //                        flow.SelectedScopes = oauth.PreselectedScopes;
    //                });
    //                break;

    //            case OAuth2FlowType.Implicit:
    //                scalarOptions.AddImplicitFlow(oauth.SchemeName, flow =>
    //                {
    //                    if (!string.IsNullOrEmpty(oauth.PrefilledClientId))
    //                        flow.ClientId = oauth.PrefilledClientId;
    //                    if (oauth.PreselectedScopes.Count > 0)
    //                        flow.SelectedScopes = oauth.PreselectedScopes;
    //                });
    //                break;
    //        }
    //    }

    //    // Set preferred security schemes
    //    if (preferredSchemes.Count > 0)
    //    {
    //        scalarOptions.AddPreferredSecuritySchemes([.. preferredSchemes]);
    //    }

    //    // Enable persistent authentication if configured
    //    if (security.EnablePersistentAuthentication)
    //    {
    //        scalarOptions.EnablePersistentAuthentication();
    //    }
    //}

    private static global::Scalar.AspNetCore.ScalarTheme MapTheme(ScalarTheme theme)
    {
        return theme switch
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
}
