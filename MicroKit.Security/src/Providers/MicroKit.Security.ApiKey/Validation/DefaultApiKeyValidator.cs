using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Validation;
using MicroKit.Security.ApiKey.Options;
using MicroKit.Security.ApiKey.Stores;
using MicroKit.Security.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace MicroKit.Security.ApiKey.Validation;

public sealed class DefaultApiKeyValidator(
    IApiKeyStore store,
    IOptions<ApiKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<DefaultApiKeyValidator> logger) : IApiKeyValidator
{
    private readonly ApiKeyOptions _options = options.Value;

    #region IApiKeyValidator Implementation

    // Surcharge String : Délègue au Span (zéro allocation supplémentaire)
    public ValueTask<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken ct = default)
        => ValidateAsync(apiKey.AsSpan(), ct);

    // Surcharge UTF-8 : Convertit en Char via un buffer de stack (Performance maximale)
    public ValueTask<ApiKeyValidationResult> ValidateAsync(ReadOnlySpan<byte> apiKeyUtf8, CancellationToken ct = default)
    {
        if (apiKeyUtf8.Length > 256) // Sécurité contre stack overflow
            return ValidateAsync(Encoding.UTF8.GetString(apiKeyUtf8), ct);

        Span<char> charBuffer = stackalloc char[apiKeyUtf8.Length];
        int written = Encoding.UTF8.GetChars(apiKeyUtf8, charBuffer);

        // On passe la main à la méthode async avec une string
        return ValidateAsync(charBuffer[..written], ct);
    }

    // MÉTHODE PRINCIPALE (Logic Engine)
    public  ValueTask<ApiKeyValidationResult> ValidateAsync(ReadOnlySpan<char> apiKey, CancellationToken ct = default)
    {
        // On effectue les validations synchrones sur le Span ici
        if (!ValidateFormat(apiKey)) return ValueTask.FromResult(ApiKeyValidationResult.Invalid());

        // On calcule le hash de manière synchrone (stackalloc autorisé ici)
        string lookupKey = ComputeHashIfRequired(apiKey);

        // On passe la main à la méthode async avec une string
        return ValidateInternalAsync(lookupKey, ct);
    }

    #endregion

    #region Private Helpers

    // LE CŒUR ASYNCHRONE (Ne manipule que des types autorisés : string)
    private async ValueTask<ApiKeyValidationResult> ValidateInternalAsync(string lookupKey, CancellationToken ct)
    {
        var record = await store.GetByHashedKeyAsync(lookupKey, ct);

        if (record is null) return ApiKeyValidationResult.Invalid();

        var now = timeProvider.GetUtcNow();
        if (!record.IsActive) return ApiKeyValidationResult.Revoked();

        // Logique d'expiration...
        if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= now)
        {
            var graceLimit = record.ExpiresAt.Value.Add(_options.Validation.AllowExpiredKeyGracePeriod);
            if (now > graceLimit) return ApiKeyValidationResult.Expired();
        }

        UpdateLastUsed(record, now);

        return ApiKeyValidationResult.Success(record.ToSecurityPrincipal(), CreateMetadata(record, now));
    }

    private bool ValidateFormat(ReadOnlySpan<char> apiKey)
    {
        var prefix = _options.Validation.KeyPrefix.AsSpan();
        if (apiKey.Length < prefix.Length + _options.Validation.MinKeyLength) return false;
        if (prefix.Length > 0 && !apiKey.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return true;
    }

    private string ComputeHashIfRequired(ReadOnlySpan<char> apiKey)
    {
        if (!_options.Security.HashKeys)
            return new string(apiKey);

        int bufferSize = _options.Security.HashAlgorithm == ApiKeyHashAlgorithms.SHA512 ? 128 : 64;
        Span<char> hashBuffer = stackalloc char[bufferSize];

        if (SecureHasher.TryComputeHash(apiKey, hashBuffer, _options.Security.HashAlgorithm))
        {
            return new string(hashBuffer);
        }

        throw new InvalidOperationException("Critical error during API key hashing.");
    }

    private void UpdateLastUsed(Models.ApiKeyRecord record, DateTimeOffset now)
    {
        // On ne met à jour que toutes les 5 minutes pour préserver la DB
        if (now - record.LastUsedAt > TimeSpan.FromMinutes(5))
        {
            _ = Task.Run(async () =>
            {
                try { await store.UpdateLastUsedAsync(record.Id, now, CancellationToken.None); }
                catch (Exception ex) { logger.LogError(ex, "Failed to update last used for {Id}", record.Id); }
            });
        }
    }

    private IReadOnlyDictionary<string, object> CreateMetadata(Models.ApiKeyRecord record, DateTimeOffset now)
    {
        return new Dictionary<string, object>
        {
            ["key_id"] = record.Id,
            ["tenant_id"] = record.TenantId ?? string.Empty,
            ["is_grace_period"] = record.ExpiresAt.HasValue && record.ExpiresAt.Value <= now
        };
    }

    #endregion
}
