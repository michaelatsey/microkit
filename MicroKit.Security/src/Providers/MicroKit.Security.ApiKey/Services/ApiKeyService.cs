
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MicroKit.Security.ApiKey.Models;
using MicroKit.Security.ApiKey.Options;
using MicroKit.Security.ApiKey.Stores;
using MicroKit.Security.Core.Utilities;
using MicroKit.Security.Abstractions.Enums;

namespace MicroKit.Security.ApiKey.Services;
/// <summary>Service for creating, validating, rotating, and revoking API keys.</summary>
public sealed class ApiKeyService(
    IApiKeyStore store,
    IOptions<ApiKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<ApiKeyService> logger) : IApiKeyService
{
    private readonly ApiKeyOptions _options = options.Value;

    /// <inheritdoc/>
    public async ValueTask<ApiKeyCreationResult> CreateKeyAsync(
        CreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var keyId = Guid.NewGuid().ToString("N");

        var plainTextKey = SecureTokenGenerator.GenerateApiKey(
            _options.Validation.KeyPrefix,
            _options.Validation.MinKeyLength);

        var hashedKey = HashKeyIfEnabled(plainTextKey);

        int prefixLen = _options.Validation.KeyPrefix.Length;
        int displayLen = Math.Min(plainTextKey.Length, prefixLen + 8);
        var displayPrefix = plainTextKey[..displayLen];

        var record = new ApiKeyRecord
        {
            Id = keyId,
            HashedKey = hashedKey,
            Prefix = displayPrefix,
            Name = request.Name,
            OwnerId = request.OwnerId,
            OwnerName = request.OwnerName,
            TenantId = request.TenantId,
            Scopes = request.Scopes,
            Roles = request.Roles,
            Metadata = request.Metadata,
            IsActive = true,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt ?? (now + _options.Validation.DefaultKeyLifetime),
            RateLimit = request.RateLimit ?? _options.Performance.RateLimit,
            RateLimitWindow = request.RateLimitWindow ?? _options.Performance.RateLimitWindow,
            AllowedIpAddresses = request.AllowedIpAddresses
        };

        await store.CreateAsync(record, cancellationToken);

        logger.LogInformation("API key created: {KeyId} (Prefix: {Prefix})", keyId, displayPrefix);

        return new ApiKeyCreationResult(keyId, plainTextKey, displayPrefix, now, record.ExpiresAt);
    }

    /// <summary>
    /// Validates an API key.
    /// </summary>
    /// <param name="apiKey">Plain text API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Key record if valid.
    /// </returns>
    public async ValueTask<ApiKeyRecord?> ValidateKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(apiKey)) return null;

        var hashedKey = HashKeyIfEnabled(apiKey);
        var record = await store.GetByHashedKeyAsync(hashedKey, cancellationToken);

        if (record is null || !record.IsActive) return null;

        var now = timeProvider.GetUtcNow();

        if (record.ExpiresAt.HasValue)
        {
            var expiryWithGrace = record.ExpiresAt.Value.Add(_options.Validation.AllowExpiredKeyGracePeriod);
            if (expiryWithGrace <= now) return null;
        }

        return record;
    }

    /// <summary>
    /// Gets all keys for an owner.
    /// </summary>
    /// <param name="ownerId">Owner ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Collection of API key info (without secrets).
    /// </returns>
    public async ValueTask<IReadOnlyList<ApiKeyInfo>> GetKeysForOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var records = await store.GetByOwnerAsync(ownerId, cancellationToken);

        return [.. records.Select(r => new ApiKeyInfo(
            r.Id, r.Prefix, r.Name, r.IsActive, r.CreatedAt, r.ExpiresAt, r.LastUsedAt, r.Scopes
        ))];
    }

    /// <summary>
    /// Rotates an existing API key.
    /// </summary>
    /// <param name="keyId">Key ID to rotate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// New key details.
    /// </returns>
    /// <exception cref="KeyNotFoundException">API key '{keyId}' not found</exception>
    public async ValueTask<ApiKeyCreationResult> RotateKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        var existingKey = await store.GetByIdAsync(keyId, cancellationToken)
            ?? throw new KeyNotFoundException($"API key '{keyId}' not found");

        var result = await CreateKeyAsync(MapToRequest(existingKey), cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (_options.Security.EnableKeyRotation && _options.Security.RotationGracePeriod > TimeSpan.Zero)
        {
            var deferredExpiry = now.Add(_options.Security.RotationGracePeriod);
            var updatedOld = existingKey with { ExpiresAt = deferredExpiry, IsActive = true };
            await store.UpdateAsync(updatedOld, cancellationToken);

            logger.LogInformation("Rotation: API key {OldKeyId} scheduled for revocation at {GraceTime}", keyId, deferredExpiry);
        }
        else
        {
            await store.RevokeAsync(keyId, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    /// <param name="keyId">Key ID to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns></returns>
    public async ValueTask RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await store.RevokeAsync(keyId, cancellationToken);
        logger.LogWarning("API key revoked: {KeyId}", keyId);
    }

    private string HashKeyIfEnabled(string plainTextKey)
    {
        if (!_options.Security.HashKeys) return plainTextKey;

        int bufferSize = _options.Security.HashAlgorithm == ApiKeyHashAlgorithms.SHA512 ? 128 : 64;
        Span<char> hashBuffer = stackalloc char[bufferSize];

        if (!SecureHasher.TryComputeHash(plainTextKey.AsSpan(), hashBuffer, _options.Security.HashAlgorithm))
        {
            throw new InvalidOperationException("Critical: Failed to compute secure hash.");
        }

        return new string(hashBuffer);
    }

    /// <summary>
    /// Maps to request.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns></returns>
    private static CreateApiKeyRequest MapToRequest(ApiKeyRecord record) => new()
    {
        Name = record.Name,
        OwnerId = record.OwnerId,
        OwnerName = record.OwnerName,
        TenantId = record.TenantId,
        Scopes = record.Scopes,
        Roles = record.Roles,
        Metadata = record.Metadata,
        ExpiresAt = record.ExpiresAt,
        AllowedIpAddresses = record.AllowedIpAddresses,
        RateLimit = record.RateLimit,
        RateLimitWindow = record.RateLimitWindow
    };
}