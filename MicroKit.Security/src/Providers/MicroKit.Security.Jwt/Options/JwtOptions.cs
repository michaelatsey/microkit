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
    public const string SectionName = "MicroKit:Security:Jwt";

    /// <summary>
    /// Configuration de l'extraction (Header, Scheme).
    /// </summary>
    public JwtExtractionOptions Extraction { get; init; } = new();

    /// <summary>
    /// Configuration des contraintes de validation (Issuer, Audience, Keys).
    /// </summary>
    public JwtValidationOptions Validation { get; init; } = new();

    /// <summary>
    /// Configuration des mappings de claims (Sub, Roles, TenantId).
    /// </summary>
    public JwtClaimsMappingOptions ClaimsMapping { get; init; } = new();

    /// <summary>
    /// Configuration des clés et de la signature.
    /// </summary>
    public JwtSigningOptions Signing { get; init; } = new();

    /// <summary>
    /// Configuration unifiée du cache pour le schéma JWT.
    /// </summary>
    public CacheOptions Cache { get; init; } = new();
}

public sealed class JwtExtractionOptions
{
    /// <summary>
    /// Nom du schéma dans le header Authorization. Par défaut "Bearer".
    /// </summary>
    [Required]
    public string AuthorizationScheme { get; set; } = "Bearer";

    /// <summary>
    /// Si vrai, le middleware tente d'extraire le TenantId du token.
    /// </summary>
    public bool ExtractTenantFromToken { get; set; } = true;
}

public sealed class JwtValidationOptions
{
    public bool ValidateIssuer { get; set; } = true;
    public string Issuer { get; set; } = "MicroKit";
    public List<string> ValidIssuers { get; set; } = [];

    public bool ValidateAudience { get; set; } = true;
    public string Audience { get; set; } = "MicroKit";
    public List<string> ValidAudiences { get; set; } = [];

    public bool ValidateLifetime { get; set; } = true;
    public bool ValidateIssuerSigningKey { get; set; } = true;


    [Range(0, 60)]
    public int ClockSkewMinutes { get; set; } = 5;

    // --- Nouveaux paramètres ajoutés ici ---
    [Range(1, 1440)] // Max 24h pour un Access Token par défaut
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    [Range(1, 365)]
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// Exige la présence d'un claim TenantId pour valider le token.
    /// </summary>
    public bool RequireTenantClaim { get; set; } = false;
}

public sealed class JwtClaimsMappingOptions
{
    public string UserIdClaim { get; set; } = "sub";
    public string UserNameClaim { get; set; } = "name";
    public string EmailClaim { get; set; } = "email";
    public string RolesClaim { get; set; } = "roles";
    public string TenantIdClaim { get; set; } = "tid";
}

public sealed class JwtSigningOptions
{
    public string Algorithm { get; set; } = SecurityAlgorithms.HmacSha256;

    // Pour HMAC
    public string? SecretKey { get; set; }

    // Pour RSA / Asymétrique
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }

    // Pour JWKS externe
    public string? JwksUri { get; set; }
    public int JwksKeyRefreshMinutes { get; set; } = 24; // Par défaut 24h, car une clé change rarement

    public bool IsAsymmetric => !string.IsNullOrEmpty(PublicKey) ||
                                Algorithm.StartsWith("RS") ||
                                Algorithm.StartsWith("PS");
}
