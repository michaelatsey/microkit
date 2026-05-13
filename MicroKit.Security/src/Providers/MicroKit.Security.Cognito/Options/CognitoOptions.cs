using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Options;
using System.ComponentModel.DataAnnotations;

namespace MicroKit.Security.Cognito.Options;

/// <summary>Configuration options for AWS Cognito token validation.</summary>
public sealed class CognitoOptions : ICacheableOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "MicroKit:Security:Cognito";

    /// <summary>Gets or sets the AWS region (e.g. <c>us-east-1</c>).</summary>
    [Required]
    public string Region { get; set; } = string.Empty;

    /// <summary>Gets or sets the Cognito User Pool ID (e.g. <c>us-east-1_AbCdEfGhI</c>).</summary>
    [Required]
    public string UserPoolId { get; set; } = string.Empty;

    /// <summary>Gets or sets the App Client ID registered in the User Pool.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the token issuer is validated.</summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>Gets or sets whether token lifetime is validated.</summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>Gets or sets the allowed clock skew in minutes.</summary>
    [Range(0, 60)]
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>Gets or sets how often (in minutes) the JWKS keys are refreshed from Cognito.</summary>
    public int JwksKeyRefreshMinutes { get; set; } = 60;

    /// <summary>Gets the claim name used to extract the user identifier.</summary>
    public string UserIdClaim { get; set; } = "sub";

    /// <summary>Gets the claim name used to extract the user name.</summary>
    public string UserNameClaim { get; set; } = "cognito:username";

    /// <summary>Gets the claim name used to extract groups (mapped to roles).</summary>
    public string GroupsClaim { get; set; } = "cognito:groups";

    /// <summary>Gets the JWKS endpoint URL for this user pool.</summary>
    public string JwksUri =>
        $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}/.well-known/jwks.json";

    /// <summary>Gets the expected token issuer URL for this user pool.</summary>
    public string Issuer =>
        $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";

    /// <summary>Gets or sets the optional two-level cache configuration for validation results.</summary>
    public CacheOptions Cache { get; init; } = new();
}
