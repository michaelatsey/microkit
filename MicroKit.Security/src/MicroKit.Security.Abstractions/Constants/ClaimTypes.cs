namespace MicroKit.Security.Abstractions.Constants;

/// <summary>
/// Types de claims standard utilisés dans l'écosystème MicroKit.Security.
/// Fournit des constantes pour éviter les erreurs de frappe et assurer la cohérence.
/// </summary>
public static class ClaimTypes
{
    /// <summary>Identifiant du sujet (subject).</summary>
    public const string Subject = "sub";

    /// <summary>Identifiant unique de l'utilisateur.</summary>
    public const string UserId = "user_id";

    /// <summary>Adresse email.</summary>
    public const string Email = "email";

    /// <summary>Email vérifié.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>Nom complet.</summary>
    public const string Name = "name";

    /// <summary>Prénom.</summary>
    public const string GivenName = "given_name";

    /// <summary>Nom de famille.</summary>
    public const string FamilyName = "family_name";

    /// <summary>Rôle de l'utilisateur.</summary>
    public const string Role = "role";

    /// <summary>Rôles multiples (claim répétable).</summary>
    public const string Roles = "roles";

    /// <summary>Scope OAuth2.</summary>
    public const string Scope = "scope";

    /// <summary>Identifiant du tenant.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Identifiant du client (application).</summary>
    public const string ClientId = "client_id";

    /// <summary>Nom du client (application).</summary>
    public const string ClientName = "client_name";

    /// <summary>Timestamp d'expiration.</summary>
    public const string Expiration = "exp";

    /// <summary>Timestamp d'émission.</summary>
    public const string IssuedAt = "iat";

    /// <summary>Timestamp de validité (not before).</summary>
    public const string NotBefore = "nbf";

    /// <summary>Émetteur du token.</summary>
    public const string Issuer = "iss";

    /// <summary>Audience du token.</summary>
    public const string Audience = "aud";

    /// <summary>Identifiant unique du token (JWT ID).</summary>
    public const string JwtId = "jti";

    /// <summary>Niveau de permission.</summary>
    public const string PermissionLevel = "permission_level";

    /// <summary>Permissions spécifiques.</summary>
    public const string Permissions = "permissions";
}
