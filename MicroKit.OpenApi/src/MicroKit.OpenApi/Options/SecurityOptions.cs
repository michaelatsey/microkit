namespace MicroKit.OpenApi.Options;

/// <summary>
/// Base class for OpenAPI security scheme configuration.
/// </summary>
public abstract class SecuritySchemeOptions
{
    /// <summary>
    /// Gets the security scheme type.
    /// </summary>
    public abstract SecurityType Type { get; }

    /// <summary>
    /// Gets or sets the unique scheme name used as the key in OpenAPI Components (e.g., "Bearer", "ApiKey").
    /// </summary>
    public string SchemeName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the description shown in the OpenAPI document for this scheme.
    /// </summary>
    public string Description { get; set; } = default!;
}

/// <summary>
/// Security scheme type enumeration.
/// </summary>
public enum SecurityType
{
    /// <summary>No security scheme.</summary>
    None,
    /// <summary>HTTP Bearer / JWT authentication.</summary>
    Bearer,
    /// <summary>OAuth 2.0 authentication.</summary>
    OAuth2,
    /// <summary>API Key authentication.</summary>
    ApiKey,
    /// <summary>OpenID Connect authentication.</summary>
    OpenIdConnect
}

/// <summary>
/// Configuration for a HTTP Bearer / JWT security scheme.
/// </summary>
public sealed class BearerSecurityOptions : SecuritySchemeOptions
{
    /// <inheritdoc />
    public override SecurityType Type => SecurityType.Bearer;

    /// <summary>
    /// Gets or sets the required scopes for this scheme.
    /// </summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>
    /// Gets or sets the bearer token format shown in the UI (default: <c>JWT</c>).
    /// </summary>
    public string BearerFormat { get; set; } = "JWT";

    /// <summary>
    /// Gets or sets the OpenID Connect discovery URL. When set, some UIs can auto-fetch metadata.
    /// </summary>
    public string? OpenIdConnectUrl { get; set; }

    /// <summary>
    /// Gets or sets a pre-filled token value for the Scalar UI (development only — never use real tokens).
    /// </summary>
    public string? PrefilledValue { get; set; }

    /// <summary>Initializes a new instance with sensible defaults.</summary>
    public BearerSecurityOptions()
    {
        SchemeName = "Bearer";
        Description = "JWT Authorization header using the Bearer scheme.";
    }
}

/// <summary>
/// Configuration for an OAuth 2.0 security scheme.
/// </summary>
public sealed class OAuth2SecurityOptions : SecuritySchemeOptions
{
    /// <inheritdoc />
    public override SecurityType Type => SecurityType.OAuth2;

    /// <summary>
    /// Gets or sets the authorization URL. Required for <see cref="OAuth2FlowType.AuthorizationCode"/> and <see cref="OAuth2FlowType.Implicit"/> flows.
    /// </summary>
    public string? AuthorizationUrl { get; set; }

    /// <summary>
    /// Gets or sets the token URL. Required for <see cref="OAuth2FlowType.AuthorizationCode"/>, <see cref="OAuth2FlowType.ClientCredentials"/>, and <see cref="OAuth2FlowType.Password"/> flows.
    /// </summary>
    public string? TokenUrl { get; set; }

    /// <summary>
    /// Gets or sets the refresh token URL.
    /// </summary>
    public string? RefreshUrl { get; set; }

    /// <summary>
    /// Gets or sets the available scopes. Key: scope name, Value: description.
    /// Use double-underscore (<c>__</c>) in keys as a stand-in for colon (<c>:</c>) when loading from JSON configuration.
    /// </summary>
    public Dictionary<string, string> Scopes { get; set; } = [];

    /// <summary>
    /// Gets or sets the OAuth 2.0 flow type.
    /// </summary>
    public OAuth2FlowType FlowType { get; set; } = OAuth2FlowType.AuthorizationCode;

    /// <summary>
    /// Gets or sets a pre-filled Client ID for the Scalar UI (development only).
    /// </summary>
    public string? PrefilledClientId { get; set; }

    /// <summary>
    /// Gets or sets a pre-filled Client Secret for the Scalar UI (development only).
    /// </summary>
    public string? PrefilledClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a pre-filled username for the Password flow (development only).
    /// </summary>
    public string? PrefilledUsername { get; set; }

    /// <summary>
    /// Gets or sets a pre-filled password for the Password flow (development only).
    /// </summary>
    public string? PrefilledPassword { get; set; }

    /// <summary>
    /// Gets or sets whether to enable PKCE (Proof Key for Code Exchange). Defaults to <c>true</c> for the AuthorizationCode flow.
    /// </summary>
    public bool EnablePkce { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of scopes pre-selected in the Scalar UI.
    /// </summary>
    public List<string> PreselectedScopes { get; set; } = [];

    /// <summary>Initializes a new instance with sensible defaults.</summary>
    public OAuth2SecurityOptions()
    {
        SchemeName = "OAuth2";
        Description = "OAuth 2.0 Authentication";
    }
}

/// <summary>
/// OAuth 2.0 flow types.
/// </summary>
public enum OAuth2FlowType
{
    /// <summary>Authorization Code flow (recommended for server-side apps).</summary>
    AuthorizationCode,
    /// <summary>Implicit flow (legacy — prefer AuthorizationCode with PKCE).</summary>
    Implicit,
    /// <summary>Resource Owner Password Credentials flow.</summary>
    Password,
    /// <summary>Client Credentials flow (machine-to-machine).</summary>
    ClientCredentials
}

/// <summary>
/// Configuration for an API Key security scheme.
/// </summary>
public sealed class ApiKeySecurityOptions : SecuritySchemeOptions
{
    /// <inheritdoc />
    public override SecurityType Type => SecurityType.ApiKey;

    /// <summary>
    /// Gets or sets the header or query parameter name that carries the API key.
    /// </summary>
    public string Name { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets where the API key is located in the request.
    /// </summary>
    public ApiKeyLocation Location { get; set; } = ApiKeyLocation.Header;

    /// <summary>
    /// Gets or sets a pre-filled API key value for the Scalar UI (development only — never use real keys).
    /// </summary>
    public string? PrefilledValue { get; set; }

    /// <summary>Initializes a new instance with sensible defaults.</summary>
    public ApiKeySecurityOptions()
    {
        SchemeName = "ApiKey";
        Description = "API Key authentication.";
    }
}

/// <summary>
/// Location of the API key within an HTTP request.
/// </summary>
public enum ApiKeyLocation
{
    /// <summary>API key is sent in an HTTP header.</summary>
    Header,
    /// <summary>API key is sent as a query string parameter.</summary>
    Query,
    /// <summary>API key is sent in a cookie.</summary>
    Cookie
}
