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

    /// <summary>Gets or sets options governing how API keys are extracted from HTTP requests.</summary>
    public ExtractionOptions Extraction { get; init; } = new();

    /// <summary>Gets or sets options governing API key format and lifetime constraints.</summary>
    public ValidationOptions Validation { get; init; } = new();

    /// <summary>Gets or sets options governing API key hashing and rotation security policies.</summary>
    public SecurityOptions Security { get; init; } = new();

    /// <summary>Gets or sets options governing rate limiting and caching performance policies.</summary>
    public PerformanceOptions Performance { get; init; } = new();

    /// <summary>Gets or sets the unified cache configuration for API key validation results.</summary>
    public CacheOptions Cache { get; init; } = new();
}

/// <summary>Options governing how API keys are extracted from HTTP requests.</summary>
public sealed class ExtractionOptions
{
    /// <summary>Gets or sets the HTTP header name used to pass API keys (e.g. <c>X-API-Key</c>).</summary>
    [Required]
    public string HeaderName { get; set; } = "X-API-Key";

    /// <summary>Gets or sets the query string parameter name used to pass API keys (e.g. <c>api_key</c>).</summary>
    public string? QueryParameterName { get; set; } = "api_key";

    /// <summary>Gets or sets the scheme prefix expected in the <c>Authorization</c> header (e.g. <c>ApiKey mk_…</c>).</summary>
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

    /// <summary>Gets or sets the grace period after key expiry during which the key is still accepted.</summary>
    public TimeSpan AllowExpiredKeyGracePeriod { get; init; } = TimeSpan.Zero;
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

}