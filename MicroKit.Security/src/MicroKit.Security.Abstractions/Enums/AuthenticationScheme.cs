namespace MicroKit.Security.Abstractions.Enums;

/// <summary>
/// Définit les schémas d'authentification supportés par l'écosystème MicroKit.Security.
/// </summary>
public enum AuthenticationScheme : byte
{
    /// <summary>Aucun schéma d'authentification (anonyme).</summary>
    None = 0,

    /// <summary>Authentification par clé API.</summary>
    ApiKey = 1,

    /// <summary>Authentification par JWT (JSON Web Token).</summary>
    Jwt = 2,

    /// <summary>Authentification OAuth 2.0.</summary>
    OAuth2 = 3,

    /// <summary>Authentification AWS Cognito.</summary>
    Cognito = 4,

    /// <summary>Authentification Azure Active Directory.</summary>
    AzureAd = 5
}
