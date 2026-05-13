namespace MicroKit.Security.Jwt.Options;

using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Options;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// JWT authentication configuration options.
/// </summary>
public sealed class JwtOptions: ICacheableOptions
{
    /// <summary>The configuration section name for JWT options.</summary>
    public const string SectionName = "MicroKit:Security:Jwt";

    /// <summary>Gets or sets options governing how JWT tokens are extracted from HTTP requests.</summary>
    public JwtExtractionOptions Extraction { get; init; } = new();

    /// <summary>Gets or sets options governing issuer, audience, lifetime, and key validation.</summary>
    public JwtValidationOptions Validation { get; init; } = new();

    /// <summary>Gets or sets options mapping JWT claim names to MicroKit principal properties.</summary>
    public JwtClaimsMappingOptions ClaimsMapping { get; init; } = new();

    /// <summary>Gets or sets options governing JWT signing key material and algorithm selection.</summary>
    public JwtSigningOptions Signing { get; init; } = new();

    /// <summary>Gets or sets the unified cache configuration for JWT validation results.</summary>
    public CacheOptions Cache { get; init; } = new();
}

/// <summary>Options governing JWT token extraction from the HTTP Authorization header.</summary>
public sealed class JwtExtractionOptions
{
    /// <summary>Gets or sets the scheme prefix expected in the <c>Authorization</c> header (default: <c>Bearer</c>).</summary>
    [Required]
    public string AuthorizationScheme { get; set; } = "Bearer";

    /// <summary>Gets or sets whether the middleware attempts to extract a tenant identifier from the token.</summary>
    public bool ExtractTenantFromToken { get; set; } = true;
}

/// <summary>Options governing JWT signature, issuer, audience, and lifetime validation.</summary>
public sealed class JwtValidationOptions
{
    /// <summary>Gets or sets whether the token issuer is validated.</summary>
    public bool ValidateIssuer { get; set; } = true;
    /// <summary>Gets or sets the expected issuer claim value.</summary>
    public string Issuer { get; set; } = "MicroKit";
    /// <summary>Gets or sets additional valid issuer values.</summary>
    public List<string> ValidIssuers { get; set; } = [];

    /// <summary>Gets or sets whether the token audience is validated.</summary>
    public bool ValidateAudience { get; set; } = true;
    /// <summary>Gets or sets the expected audience claim value.</summary>
    public string Audience { get; set; } = "MicroKit";
    /// <summary>Gets or sets additional valid audience values.</summary>
    public List<string> ValidAudiences { get; set; } = [];

    /// <summary>Gets or sets whether token lifetime is validated.</summary>
    public bool ValidateLifetime { get; set; } = true;
    /// <summary>Gets or sets whether the issuer signing key is validated.</summary>
    public bool ValidateIssuerSigningKey { get; set; } = true;

    /// <summary>Gets or sets the allowed clock skew in minutes to tolerate minor time differences.</summary>
    [Range(0, 60)]
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>Gets or sets the access token expiration in minutes.</summary>
    [Range(1, 1440)]
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>Gets or sets the refresh token expiration in days.</summary>
    [Range(1, 365)]
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>Gets or sets whether a tenant identifier claim is required for the token to be considered valid.</summary>
    public bool RequireTenantClaim { get; set; } = false;
}

/// <summary>Options mapping JWT claim names to MicroKit principal properties.</summary>
public sealed class JwtClaimsMappingOptions
{
    /// <summary>Gets or sets the claim name used as the user identifier.</summary>
    public string UserIdClaim { get; set; } = "sub";
    /// <summary>Gets or sets the claim name used as the display name.</summary>
    public string UserNameClaim { get; set; } = "name";
    /// <summary>Gets or sets the claim name used for the email address.</summary>
    public string EmailClaim { get; set; } = "email";
    /// <summary>Gets or sets the claim name used for roles.</summary>
    public string RolesClaim { get; set; } = "roles";
    /// <summary>Gets or sets the claim name used for the tenant identifier.</summary>
    public string TenantIdClaim { get; set; } = "tid";
}

/// <summary>Options governing JWT signing key material and algorithm selection.</summary>
public sealed class JwtSigningOptions
{
    /// <summary>Gets or sets the signing algorithm (e.g. <c>HS256</c>, <c>RS256</c>).</summary>
    public string Algorithm { get; set; } = SecurityAlgorithms.HmacSha256;

    /// <summary>Gets or sets the HMAC secret key (symmetric signing).</summary>
    public string? SecretKey { get; set; }

    /// <summary>Gets or sets the PEM-encoded RSA public key (used for token validation).</summary>
    public string? PublicKey { get; set; }
    /// <summary>Gets or sets the PEM-encoded RSA private key (used for token generation).</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Gets or sets the JWKS endpoint URI for remote key discovery.</summary>
    public string? JwksUri { get; set; }
    /// <summary>Gets or sets how often (in minutes) the JWKS keys are refreshed.</summary>
    public int JwksKeyRefreshMinutes { get; set; } = 24;

    /// <summary>Gets a value indicating whether asymmetric (RSA/PS) signing is configured.</summary>
    public bool IsAsymmetric => !string.IsNullOrEmpty(PublicKey) ||
                                Algorithm.StartsWith("RS") ||
                                Algorithm.StartsWith("PS");
}
