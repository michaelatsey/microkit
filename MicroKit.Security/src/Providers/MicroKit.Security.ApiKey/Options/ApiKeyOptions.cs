using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using System.ComponentModel.DataAnnotations;

namespace MicroKit.Security.ApiKey.Options;


public sealed class ApiKeyOptions: ICacheableOptions
{
    public const string SectionName = "MicroKit:Security:ApiKey";

    /// <summary>
    /// Configuration de l'extraction de la clé depuis la requête HTTP.
    /// </summary>
    public ExtractionOptions Extraction { get; init; } = new();

    /// <summary>
    /// Configuration des contraintes de format de la clé.
    /// </summary>
    public ValidationOptions Validation { get; init; } = new();

    /// <summary>
    /// Configuration de la sécurité (Hachage, Rotation).
    /// </summary>
    public SecurityOptions Security { get; init; } = new();

    /// <summary>
    /// Configuration de la performance (Cache, Rate Limiting).
    /// </summary>
    public PerformanceOptions Performance { get; init; } = new();

    // On remplace PerformanceOptions par ton CacheOptions global
    public CacheOptions Cache { get; init; } = new();
}

public sealed class ExtractionOptions
{
    /// <summary>
    /// Nom du header HTTP personnalisé. Ex: "X-API-Key"
    /// </summary>
    [Required]
    public string HeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// Nom du paramètre dans l'URL. Ex: "?api_key=..."
    /// </summary>
    public string? QueryParameterName { get; set; } = "api_key";

    /// <summary>
    /// Le schéma utilisé dans le header standard 'Authorization'. 
    /// Exemple : "Authorization: ApiKey mk_123..." -> ici le scheme est "ApiKey"
    /// </summary>
    public string AuthorizationScheme { get; set; } = "ApiKey";
}

public sealed class ValidationOptions
{
    [Required] public string KeyPrefix { get; set; } = "mk_";
    [Range(16, 128)] public int MinKeyLength { get; init; } = 32;
    public TimeSpan DefaultKeyLifetime { get; init; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Durée de tolérance après l'expiration de la clé.
    /// </summary>
    /// <value>
    /// The allow expired key grace period.
    /// </value>
    public TimeSpan AllowExpiredKeyGracePeriod { get; init; } = TimeSpan.Zero; // Par défaut à 0 pour être strict
}

public sealed class SecurityOptions
{
    public bool HashKeys { get; set; } = true;
    public ApiKeyHashAlgorithms HashAlgorithm { get; set; } = ApiKeyHashAlgorithms.SHA256;
    public bool EnableKeyRotation { get; init; } = true;
    public TimeSpan RotationGracePeriod { get; init; } = TimeSpan.FromHours(24);
}

public sealed class PerformanceOptions
{
    public int RateLimit { get; set; } = 60;

    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    //public TimeSpan RateLimitPerMinute { get; init; } = TimeSpan.FromMinutes(1000);
}