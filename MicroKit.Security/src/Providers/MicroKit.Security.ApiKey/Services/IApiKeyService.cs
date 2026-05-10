
using MicroKit.Security.ApiKey.Models;

namespace MicroKit.Security.ApiKey.Services;
/// <summary>
/// Service for managing API keys.
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Creates a new API key.
    /// </summary>
    /// <param name="request">Key creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created key details (includes plain text key only on creation).</returns>
    ValueTask<ApiKeyCreationResult> CreateKeyAsync(
        CreateApiKeyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates an existing API key.
    /// </summary>
    /// <param name="keyId">Key ID to rotate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New key details.</returns>
    ValueTask<ApiKeyCreationResult> RotateKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    /// <param name="keyId">Key ID to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RevokeKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all keys for an owner.
    /// </summary>
    /// <param name="ownerId">Owner ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of API key info (without secrets).</returns>
    ValueTask<IReadOnlyList<ApiKeyInfo>> GetKeysForOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an API key.
    /// </summary>
    /// <param name="apiKey">Plain text API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Key record if valid.</returns>
    ValueTask<ApiKeyRecord?> ValidateKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new API key.
/// </summary>
public sealed record CreateApiKeyRequest
{
    /// <summary>
    /// Owner ID for the key.
    /// </summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Owner display name.
    /// </summary>
    public string? OwnerName { get; init; }

    /// <summary>
    /// Key display name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant scenarios.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Scopes to grant.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// Roles to assign.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Optional expiration time.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public int? RateLimit { get; init; }

    public TimeSpan? RateLimitWindow { get; init; }

    /// <summary>
    /// Allowed IP addresses.
    /// </summary>
    public IReadOnlyList<string>? AllowedIpAddresses { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of API key creation.
/// </summary>
public sealed record ApiKeyCreationResult(
    string Id,
    string PlainTextKey,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// API key information (without secrets).
/// </summary>
public sealed record ApiKeyInfo(
    string Id,
    string Prefix,
    string? Name,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    IReadOnlyList<string> Scopes);
