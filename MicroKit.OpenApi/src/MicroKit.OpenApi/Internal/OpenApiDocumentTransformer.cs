using MicroKit.OpenApi.Filters;
using MicroKit.OpenApi.Options;
using Microsoft.OpenApi;

namespace MicroKit.OpenApi.Internal;

/// <summary>
/// Transforms OpenAPI documents with MicroKit configuration.
/// </summary>
internal sealed class OpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly IOptions<MicroKitOpenApiOptions> _options;
    private readonly FilterRegistry _filterRegistry;
    private readonly IServiceProvider _serviceProvider;

    public OpenApiDocumentTransformer(
        IOptions<MicroKitOpenApiOptions> options,
        FilterRegistry filterRegistry,
        IServiceProvider serviceProvider)
    {
        _options = options;
        _filterRegistry = filterRegistry;
        _serviceProvider = serviceProvider;
    }

    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var documentName = context.DocumentName;
        var isDeprecated = options.DeprecatedVersions.Contains(documentName);

        // ApplyAsync info metadata
        ApplyInfoMetadata(document, context, options);
        // Set servers
        ApplyServers(document, options);


        // ApplyAsync security schemes (Microsoft.OpenApi 2.0+ API)
        ApplySecuritySchemes(document, options);

        // ApplyAsync custom document filters
        var filterContext = new DocumentFilterContext
        {
            DocumentName = documentName,
            ApiVersion = documentName,
            IsDeprecated = isDeprecated,
            ServiceProvider = _serviceProvider
        };

        foreach (var filterType in _filterRegistry.DocumentFilters)
        {
            var filter = (IOpenApiDocumentFilter)_serviceProvider.GetRequiredService(filterType);
            await filter.ApplyAsync(document, filterContext, cancellationToken);
        }
    }

    private static void ApplyInfoMetadata(
        OpenApiDocument document, 
        OpenApiDocumentTransformerContext context, 
        MicroKitOpenApiOptions options)
    {

        document.Info ??= new OpenApiInfo();
        document.Info.Title = options.Title;
        document.Info.Version = context.DocumentName;
        document.Info.Description = options.Description;

        if (options.Contact is not null)
        {
            document.Info.Contact = new OpenApiContact
            {
                Name = options.Contact.Name,
                Email = options.Contact.Email,
                Url = !string.IsNullOrWhiteSpace(options.Contact.Url)
                    ? new Uri(options.Contact.Url)
                    : null
            };
        }

        if (options.License is not null)
        {
            document.Info.License = new OpenApiLicense
            {
                Name = options.License.Name,
                Url = !string.IsNullOrWhiteSpace(options.License.Url)
                    ? new Uri(options.License.Url)
                    : null
            };
        }

        if (!string.IsNullOrWhiteSpace(options.TermsOfServiceUrl))
        {
            document.Info.TermsOfService = new Uri(options.TermsOfServiceUrl);
        }

        if (!string.IsNullOrWhiteSpace(options.ExternalDocsUrl))
        {
            document.ExternalDocs = new OpenApiExternalDocs
            {
                Url = new Uri(options.ExternalDocsUrl),
                Description = options.ExternalDocsDescription
            };
        }
    }

    private static void ApplyServers(OpenApiDocument document, MicroKitOpenApiOptions options)
    {
        if (options.Servers.Count == 0)
        {
            return;
        }

        document.Servers ??= [];
        document.Servers.Clear();

        foreach (var server in options.Servers)
        {
            document.Servers.Add(new OpenApiServer
            {
                Url = server.Url,
                Description = server.Description
            });
        }
    }

    private static void ApplySecurityDefinition(OpenApiDocument document, SecuritySchemeOptions option)
    {
        switch (option)
        {
            case BearerSecurityOptions bearer:
                RegisterBearerScheme(document, bearer);
                break;
            case OAuth2SecurityOptions oauth2:
                RegisterOAuth2Scheme(document, oauth2);
                break;
            case ApiKeySecurityOptions apiKey:
                RegisterApiKeyScheme(document, apiKey);
                break;
        }
    }

    private static void RegisterBearerScheme(OpenApiDocument document, BearerSecurityOptions bearer)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = bearer.BearerFormat,
            Description = bearer.Description
        };
        document.Components!.SecuritySchemes![bearer.SchemeName] = scheme;
    }

    private static void RegisterOAuth2Scheme(OpenApiDocument document, OAuth2SecurityOptions oauth2)
    {
        var cleanedScopes = oauth2.Scopes.ToDictionary(
            kvp => kvp.Key.Replace("__", ":"),
            kvp => kvp.Value
        );
        var flows = new OpenApiOAuthFlows();
        var flow = new OpenApiOAuthFlow
        {
            AuthorizationUrl = !string.IsNullOrWhiteSpace(oauth2.AuthorizationUrl) ? new Uri(oauth2.AuthorizationUrl) : null,
            TokenUrl = !string.IsNullOrWhiteSpace(oauth2.TokenUrl) ? new Uri(oauth2.TokenUrl) : null,
            RefreshUrl = !string.IsNullOrWhiteSpace(oauth2.RefreshUrl) ? new Uri(oauth2.RefreshUrl) : null,
            Scopes = cleanedScopes
        };

        switch (oauth2.FlowType)
        {
            case OAuth2FlowType.AuthorizationCode: flows.AuthorizationCode = flow; break;
            case OAuth2FlowType.Implicit: flows.Implicit = flow; break;
            case OAuth2FlowType.Password: flows.Password = flow; break;
            case OAuth2FlowType.ClientCredentials: flows.ClientCredentials = flow; break;
        }

        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = flows,
            Description = oauth2.Description
        };
        document.Components!.SecuritySchemes![oauth2.SchemeName] = scheme;
    }

    private static void RegisterApiKeyScheme(OpenApiDocument document, ApiKeySecurityOptions apiKey)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = apiKey.Name,
            In = apiKey.Location switch
            {
                ApiKeyLocation.Header => ParameterLocation.Header,
                ApiKeyLocation.Query => ParameterLocation.Query,
                ApiKeyLocation.Cookie => ParameterLocation.Cookie,
                _ => ParameterLocation.Header
            },
            Description = apiKey.Description
        };
        document.Components!.SecuritySchemes![apiKey.SchemeName] = scheme;
    }

    private static void ApplySecuritySchemes(OpenApiDocument document, MicroKitOpenApiOptions options)
    {
        Console.WriteLine($"Nombre de securities configurées: {options.Securities?.Count ?? 0}");
        if (options.Securities is null || options.Securities.Count == 0)
        {
            return;
        }

        // Initialize components if needed (Microsoft.OpenApi 2.0+ pattern)
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        // Initialize security list if needed
        document.Security ??= [];

        // 1. On crée UN SEUL dictionnaire d'exigence pour grouper les sécurités obligatoires
        var globalRequirement = new OpenApiSecurityRequirement();

        foreach (var securityOption in options.Securities)
        {
            Console.WriteLine($"Security configurée: {securityOption.SchemeName}");
            // 2. Enregistrement de la définition technique dans Components
            ApplySecurityDefinition(document, securityOption);

            // 3. Ajout à l'exigence globale (force Scalar à tout envoyer ensemble)
            var reference = new OpenApiSecuritySchemeReference(securityOption.SchemeName);

            // 2. LOGIQUE PRO : On détermine quels scopes afficher pour ce schéma
            var scopes = securityOption switch
            {
                OAuth2SecurityOptions oauth => [.. oauth.Scopes.Keys],
                BearerSecurityOptions bearer => bearer.Scopes, // On prend la liste de l'objet lui-même
                _ => new List<string>()
            };

            globalRequirement.Add(reference, scopes);
        }

        // 4. On ajoute ce requirement groupé au document
        document.Security = [globalRequirement];
    }
}
