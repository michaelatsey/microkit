using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Options;
using System.ComponentModel.DataAnnotations;

namespace MicroKit.Security.AzureAd.Options;

/// <summary>Configuration options for Azure Active Directory (Entra ID) token validation.</summary>
public sealed class AzureAdOptions : ICacheableOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "MicroKit:Security:AzureAd";

    /// <summary>Gets or sets the Azure AD tenant identifier (GUID or domain name, e.g. <c>contoso.onmicrosoft.com</c>). Use <c>common</c> or <c>organizations</c> for multi-tenant apps.</summary>
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the client ID (application ID) of the registered Azure AD application.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected audience. Defaults to the ClientId.</summary>
    public string? Audience { get; set; }

    /// <summary>Gets or sets additional valid audiences (e.g. the <c>api://&lt;clientId&gt;</c> URI).</summary>
    public List<string> AdditionalAudiences { get; set; } = [];

    /// <summary>Gets or sets whether the issuer is validated against the expected Azure AD issuer URL.</summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>Gets or sets whether token lifetime is validated.</summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>Gets or sets the allowed clock skew in minutes.</summary>
    [Range(0, 60)]
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>Gets or sets how often (in minutes) the JWKS keys are refreshed from Azure AD.</summary>
    public int JwksKeyRefreshMinutes { get; set; } = 60;

    /// <summary>Gets the claim name used to extract the tenant identifier from the token.</summary>
    public string TenantIdClaim { get; set; } = "tid";

    /// <summary>Gets the claim name used to extract the user identifier from the token.</summary>
    public string UserIdClaim { get; set; } = "oid";

    /// <summary>Gets the claim name used to extract the display name from the token.</summary>
    public string UserNameClaim { get; set; } = "name";

    /// <summary>Gets the OIDC discovery URL for this tenant's metadata.</summary>
    public string MetadataAddress =>
        $"https://login.microsoftonline.com/{TenantId}/v2.0/.well-known/openid-configuration";

    /// <summary>Gets the expected token issuer URL for this tenant.</summary>
    public string Issuer =>
        $"https://login.microsoftonline.com/{TenantId}/v2.0";

    /// <summary>Gets or sets the optional two-level cache configuration for validation results.</summary>
    public CacheOptions Cache { get; init; } = new();
}
