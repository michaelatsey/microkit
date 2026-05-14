using MicroKit.OpenApi.Constants;
using System.ComponentModel.DataAnnotations;

namespace MicroKit.OpenApi.Options;

/// <summary>
/// Configuration options for MicroKit OpenAPI.
/// </summary>
public sealed class MicroKitOpenApiOptions
{
    /// <summary>
    /// Gets or sets the API title.
    /// </summary>
    [Required(ErrorMessage = "API Title is required.")]
    public string Title { get; set; } = "API";

    /// <summary>
    /// Gets or sets the API description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the default API version.
    /// </summary>
    public string DefaultVersion { get; set; } = MicroKitOpenApiDefaults.DefaultApiVersion;

    /// <summary>
    /// Gets or sets the list of supported API versions.
    /// </summary>
    public List<string> SupportedVersions { get; set; } = [MicroKitOpenApiDefaults.DefaultApiVersion];

    /// <summary>
    /// Gets or sets the list of deprecated API versions.
    /// </summary>
    public List<string> DeprecatedVersions { get; set; } = [];

    /// <summary>
    /// Gets or sets the API version header key.
    /// </summary>
    public string ApiVersionHeaderKey { get; set; } = MicroKitOpenApiDefaults.DefaultApiVersionHeaderKey;
    /// <summary>
    /// Gets or sets the query string parameter name used for version negotiation.
    /// </summary>
    public string ApiVersionQueryKey { get; set; } = MicroKitOpenApiDefaults.DefaultApiVersionQueryKey;

    /// <summary>
    /// Gets or sets the media type parameter name used for version negotiation.
    /// </summary>
    public string ApiVersionMediaTypeKey { get; set; } = MicroKitOpenApiDefaults.DefaultApiVersionMediaTypeKey;

    /// <summary>   
    /// Gets or sets the contact information.
    /// </summary>
    public ContactOptions? Contact { get; set; }

    /// <summary>
    /// Gets or sets the license information.
    /// </summary>
    public LicenseOptions? License { get; set; }

    /// <summary>
    /// Gets or sets the terms of service URL.
    /// </summary>
    public string? TermsOfServiceUrl { get; set; }

    /// <summary>
    /// External documentation URL.
    /// </summary>
    public string? ExternalDocsUrl { get; set; }

    /// <summary>
    /// External documentation description.
    /// </summary>
    public string? ExternalDocsDescription { get; set; }

    /// <summary>
    /// Gets or sets whether Scalar UI is enabled.
    /// </summary>
    public bool EnableScalar { get; set; } = true;

    /// <summary>
    /// Gets or sets the Scalar endpoint path.
    /// </summary>
    public string ScalarEndpointPath { get; set; } = MicroKitOpenApiDefaults.DefaultScalarEndpointPath;

    /// <summary>
    /// Gets or sets the OpenAPI document endpoint path.
    /// </summary>
    public string OpenApiEndpointPath { get; set; } = MicroKitOpenApiDefaults.DefaultOpenApiEndpointPath;

    /// <summary>
    /// Gets or sets the Scalar theme.
    /// </summary>
    public ScalarTheme Theme { get; set; } = ScalarTheme.Default;

    /// <summary>
    /// Gets or sets the list of server URLs.
    /// </summary>
    public List<ServerOptions> Servers { get; set; } = [];

    /// <summary>
    /// Gets or sets the security configuration.
    /// </summary>
    public List<SecuritySchemeOptions>? Securities { get; } = [];

}


/// <summary>
/// Contact information options.
/// </summary>
public sealed class ContactOptions
{
    /// <summary>
    /// Gets or sets the contact name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the contact email.
    /// </summary>
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the contact URL.
    /// </summary>
    [Url(ErrorMessage = "Invalid URL format.")]
    public string? Url { get; set; }
}

/// <summary>
/// License information options.
/// </summary>
public sealed class LicenseOptions
{
    /// <summary>
    /// Gets or sets the license name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the license URL.
    /// </summary>
    [Url(ErrorMessage = "Invalid URL format.")]
    public string? Url { get; set; }
}

/// <summary>
/// Server configuration options.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Gets or sets the server URL.
    /// </summary>
    [Required]
    [Url(ErrorMessage = "Invalid URL format.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server description.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Available Scalar UI themes.
/// </summary>
public enum ScalarTheme
{
    /// <summary>Default Scalar theme.</summary>
    Default,
    /// <summary>Alternate light theme.</summary>
    Alternate,
    /// <summary>Dark moon theme.</summary>
    Moon,
    /// <summary>Purple accent theme.</summary>
    Purple,
    /// <summary>Solarized color scheme.</summary>
    Solarized,
    /// <summary>Blue planet theme.</summary>
    BluePlanet,
    /// <summary>Saturn theme.</summary>
    Saturn,
    /// <summary>Kepler theme.</summary>
    Kepler,
    /// <summary>Mars red theme.</summary>
    Mars,
    /// <summary>Deep space dark theme.</summary>
    DeepSpace
}
