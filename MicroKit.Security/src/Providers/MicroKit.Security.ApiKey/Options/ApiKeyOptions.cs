using MicroKit.Security.Abstractions.Cache;
using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Options;
using System.ComponentModel.DataAnnotations;

namespace MicroKit.Security.ApiKey.Options;


/// <summary>API key authentication configuration options.</summary>
public sealed class ApiKeyOptions: ICacheableOptions
{
    /// <summary>The configuration section name for API key options.</summary>
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

    /// <summary>Gets or sets the unified cache configuration for API key validation results.</summary>
    public CacheOptions Cache { get; init; } = new();
}

/// <summary>Options governing how API keys are extracted from HTTP requests.</summary>
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

/// <summary>Options governing API key format and lifetime constraints.</summary>
public sealed class ValidationOptions
{
    /// <summary>Gets or sets the required key prefix (e.g. <c>mk_</c>).</summary>
    [Required] public string KeyPrefix { get; set; } = "mk_";
    /// <summary>Gets or sets the minimum key length excluding the prefix.</summary>
    [Range(16, 128)] public int MinKeyLength { get; init; } = 32;
    /// <summary>Gets or sets the default lifetime of a newly created API key.</summary>
    public TimeSpan DefaultKeyLifetime { get; init; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Durée de tolérance après l'expiration de la clé.
    /// </summary>
    /// <value>
    /// The allow expired key grace period.
    /// </value>
    public TimeSpan AllowExpiredKeyGracePeriod { get; init; } = TimeSpan.Zero; // Par défaut à 0 pour être strict
}

/// <summary>Options governing API key hashing and rotation security policies.</summary>
public sealed class SecurityOptions
{
    /// <summary>Gets or sets whether keys are stored as hashes rather than plain text.</summary>
    public bool HashKeys { get; set; } = true;
    /// <summary>Gets or sets the hashing algorithm applied to keys.</summary>
    public ApiKeyHashAlgorithms HashAlgorithm { get; set; } = ApiKeyHashAlgorithms.SHA256;
    /// <summary>Gets or sets whether automatic key rotation is enabled.</summary>
    public bool EnableKeyRotation { get; init; } = true;
    /// <summary>Gets or sets how long the old key remains valid after a rotation.</summary>
    public TimeSpan RotationGracePeriod { get; init; } = TimeSpan.FromHours(24);
}

/// <summary>Options governing rate limiting and caching performance policies for API keys.</summary>
public sealed class PerformanceOptions
{
    /// <summary>Gets or sets the maximum number of requests allowed per <see cref="RateLimitWindow"/>.</summary>
    public int RateLimit { get; set; } = 60;

    /// <summary>Gets or sets the sliding time window for rate limiting.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    //public TimeSpan RateLimitPerMinute { get; init; } = TimeSpan.FromMinutes(1000);
}