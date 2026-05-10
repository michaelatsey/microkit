
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.ApiKey.Models;
/// <summary>
/// Represents a stored API key record.
/// </summary>
public sealed record ApiKeyRecord
{
    /// <summary>
    /// Unique identifier for the API key.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Hashed key value (never store plain text).
    /// </summary>
    public required string HashedKey { get; init; }

    /// <summary>
    /// Key prefix for identification (visible part).
    /// </summary>
    public required string Prefix { get; init; }

    /// <summary>
    /// Display name for the key.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Owner/user ID associated with this key.
    /// </summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Owner display name.
    /// </summary>
    public string? OwnerName { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant scenarios.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Scopes/permissions granted to this key.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// Roles assigned to this key.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Whether the key is active.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Key creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Key expiration timestamp (null for no expiration).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Last usage timestamp.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>
    /// Gets the rate limit.(null for default).
    /// </summary>
    /// <value>
    /// The rate limit.
    /// </value>
    public int RateLimit { get; init; } 

    /// <summary>
    /// Gets the rate limit window. 
    /// </summary>
    /// <value>
    /// The rate limit window.
    /// </value>
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Allowed IP addresses (null for any).
    /// </summary>
    public IReadOnlyList<string>? AllowedIpAddresses { get; init; }

    /// <summary>
    /// Converts the API key record to a security principal.
    /// </summary>
    public ISecurityPrincipal ToSecurityPrincipal()
    {
        var claims = new List<SecurityClaim>
        {
            new("api_key_id", Id),
            new("key_name", Name ?? "API Key")
        };

        if (TenantId is not null)
        {
            claims.Add(new SecurityClaim("tenant_id", TenantId));
        }

        foreach (var scope in Scopes)
        {
            claims.Add(new SecurityClaim("scope", scope));
        }

        foreach (var role in Roles)
        {
            claims.Add(new SecurityClaim("role", role));
        }

        if (Metadata is not null)
        {
            foreach (var (key, value) in Metadata)
            {
                claims.Add(new SecurityClaim($"meta:{key}", value));
            }
        }

        return new SecurityPrincipal(
            Identifier: OwnerId,
            DisplayName: OwnerName ?? Name ?? "API Key User",
            TenantId: TenantId, 
            Claims: claims);
    }
}
