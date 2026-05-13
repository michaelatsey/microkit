namespace MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Authentication schemes supported by MicroKit.Security.
/// </summary>
public enum AuthenticationScheme : byte
{
    /// <summary>No authentication scheme (anonymous).</summary>
    None = 0,

    /// <summary>API key authentication.</summary>
    ApiKey = 1,

    /// <summary>JWT (JSON Web Token) authentication.</summary>
    Jwt = 2,

    /// <summary>OAuth 2.0 authentication.</summary>
    OAuth2 = 3,

    /// <summary>AWS Cognito authentication.</summary>
    Cognito = 4,

    /// <summary>Azure Active Directory authentication.</summary>
    AzureAd = 5
}
