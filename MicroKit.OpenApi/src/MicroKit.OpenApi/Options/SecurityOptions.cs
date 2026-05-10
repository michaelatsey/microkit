namespace MicroKit.OpenApi.Options;


public abstract class SecuritySchemeOptions
{
    public abstract SecurityType Type { get; }

    /// <summary>
    /// Nom unique du schéma (ex: "Bearer", "InternalApiKey"). 
    /// Utilisé comme clé dans OpenAPI Components.
    /// </summary>
    public string SchemeName { get; set; } = default!;

    public string Description { get; set; } = default!;
}

/// <summary>
/// Security type enumeration.
/// </summary>
public enum SecurityType
{
    None,
    Bearer,
    OAuth2,
    ApiKey,
    OpenIdConnect
}

/// <summary>
/// Bearer/JWT security options.
/// </summary>
public sealed class BearerSecurityOptions: SecuritySchemeOptions
{
    public override SecurityType Type => SecurityType.Bearer;
    /// <summary>
    /// Gets or sets the scopes.
    /// </summary>
    /// <value>
    /// The scopes.
    /// </value>
    public List<string> Scopes { get; set; } = [];

    /// <summary>
    /// Gets or sets the bearer format.
    /// </summary>
    public string BearerFormat { get; set; } = "JWT";

    /// <summary>
    /// Gets or sets the OpenID Connect discovery URL.
    /// If set, some UIs can use this to fetch metadata.
    /// </summary>
    public string? OpenIdConnectUrl { get; set; }

    /// <summary>
    /// Gets or sets the prefilled token value for the UI.
    /// </summary>
    public string? PrefilledValue { get; set; }
    public BearerSecurityOptions()
    {
        SchemeName = "Bearer";
        Description = "JWT Authorization header using the Bearer scheme.";
        Scopes = [];
    }
}

/// <summary>
/// OAuth2 security options.
/// </summary>
public sealed class OAuth2SecurityOptions : SecuritySchemeOptions
{
    public override SecurityType Type => SecurityType.OAuth2;

    /// <summary>
    /// Gets or sets the authorization URL (Required for AuthorizationCode and Implicit flows).
    /// </summary>
    public string? AuthorizationUrl { get; set; }

    /// <summary>
    /// Gets or sets the token URL (Required for AuthorizationCode, ClientCredentials, and Password flows).
    /// </summary>
    public string? TokenUrl { get; set; }

    /// <summary>
    /// Gets or sets the refresh URL.
    /// </summary>
    public string? RefreshUrl { get; set; }

    /// <summary>
    /// Gets or sets the available scopes for the document (Key: scope name, Value: description).
    /// </summary>
    public Dictionary<string, string> Scopes { get; set; } = [];

    /// <summary>
    /// Gets or sets the OAuth2 flow type.
    /// </summary>
    public OAuth2FlowType FlowType { get; set; } = OAuth2FlowType.AuthorizationCode;

    // --- Nouvelles propriétés pour le pré-remplissage Scalar ---

    /// <summary>
    /// Gets or sets the prefilled Client ID.
    /// </summary>
    public string? PrefilledClientId { get; set; }

    /// <summary>
    /// Gets or sets the prefilled Client Secret.
    /// </summary>
    public string? PrefilledClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the prefilled Username (Specific to Password flow).
    /// </summary>
    public string? PrefilledUsername { get; set; }

    /// <summary>
    /// Gets or sets the prefilled Password (Specific to Password flow).
    /// </summary>
    public string? PrefilledPassword { get; set; }

    /// <summary>
    /// Gets or sets whether to enable PKCE (Proof Key for Code Exchange).
    /// Defaults to true for AuthorizationCode flow.
    /// </summary>
    public bool EnablePkce { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of scopes that should be pre-selected in the UI.
    /// </summary>
    public List<string> PreselectedScopes { get; set; } = [];

    public OAuth2SecurityOptions()
    {
        SchemeName = "OAuth2";
        Description = "OAuth2 Authentication";
    }
}

/// <summary>
/// OAuth2 flow types.
/// </summary>
public enum OAuth2FlowType
{
    AuthorizationCode,
    Implicit,
    Password,
    ClientCredentials
}

/// <summary>
/// API Key security options.
/// </summary>
public sealed class ApiKeySecurityOptions : SecuritySchemeOptions
{
    public override SecurityType Type => SecurityType.ApiKey;

    /// <summary>
    /// Gets or sets the header/query parameter name.
    /// </summary>
    public string Name { get; set; } = "X-Api-Key";
    /// <summary>
    /// Gets or sets where the API key is located.
    /// </summary>
    public ApiKeyLocation Location { get; set; } = ApiKeyLocation.Header;
    /// <summary>
    /// Pre-filled API key value for Scalar UI (development only).
    /// WARNING: Never use real keys - this is visible in the browser.
    /// </summary>
    public string? PrefilledValue { get; set; }

    public ApiKeySecurityOptions()
    {
        SchemeName = "ApiKey";
        Description = "API Key authentication.";
    }
}

/// <summary>
/// API Key location enumeration.
/// </summary>
public enum ApiKeyLocation
{
    Header,
    Query,
    Cookie
}
