using System.Net.Http;

namespace MicroKit.Auth.Supabase;

/// <summary>
/// Validates Supabase JWTs using ES256 and remote JWKS key discovery.
/// Always returns <see cref="Result{T}"/> — never throws.
/// </summary>
/// <remarks>
/// <para>
/// JWKS keys are fetched from <see cref="SupabaseAuthOptions.JwksUri"/> on first use and cached
/// for <see cref="SupabaseAuthOptions.JwksCacheDuration"/>. When token validation fails and the
/// cache has been held longer than <see cref="SupabaseAuthOptions.KeyRotationCooldown"/>, the keys
/// are force-refreshed once and validation is retried — this handles Supabase key rotation.
/// </para>
/// <para>
/// Algorithm validation is intentionally omitted from <see cref="TokenValidationParameters"/>.
/// <see cref="JsonWebTokenHandler"/> resolves the signing algorithm from the JWKS key type (EC
/// for ES256) without an explicit allow-list. Supabase issues ES256 tokens; the handler rejects
/// tokens signed with any key not present in the JWKS response.
/// </para>
/// <para>
/// This class is thread-safe and safe to register as a singleton.
/// </para>
/// </remarks>
public sealed class SupabaseJwtValidator : IJwtValidator, IDisposable
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SupabaseAuthOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly Uri _jwksUri;
    private volatile IReadOnlyList<SecurityKey>? _cachedKeys;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Initialises the validator with Supabase options and an HTTP client factory for JWKS fetching.
    /// </summary>
    public SupabaseJwtValidator(SupabaseAuthOptions options, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _jwksUri = options.JwksUri;
    }

    /// <inheritdoc />
    public async ValueTask<Result<ClaimsPrincipal>> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Failure<ClaimsPrincipal>(new InvalidTokenError(null));

        try
        {
            var keys = await GetKeysAsync(forceRefresh: false, ct).ConfigureAwait(false);
            var result = await ValidateWithKeysAsync(token, keys).ConfigureAwait(false);

            if (!result.IsValid && ShouldAttemptKeyRotationRefresh())
            {
                keys = await GetKeysAsync(forceRefresh: true, ct).ConfigureAwait(false);
                result = await ValidateWithKeysAsync(token, keys).ConfigureAwait(false);
            }

            return result.IsValid
                ? Success(new ClaimsPrincipal(result.ClaimsIdentity))
                : Failure<ClaimsPrincipal>(new InvalidTokenError(SafeFragment(token)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure<ClaimsPrincipal>(new InvalidTokenError(SafeFragment(token)));
        }
    }

    private async ValueTask<TokenValidationResult> ValidateWithKeysAsync(
        string token, IReadOnlyList<SecurityKey> keys)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
        };

        return await _handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<SecurityKey>> GetKeysAsync(
        bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _cachedKeys is { } keys && DateTimeOffset.UtcNow < _cacheExpiry)
            return keys;

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cachedKeys is { } cached && DateTimeOffset.UtcNow < _cacheExpiry)
                return cached;

            // For forced refresh, re-check the cooldown inside the lock — prevents concurrent
            // threads that all passed the outer ShouldAttemptKeyRotationRefresh() check from
            // each doing a redundant JWKS fetch.
            if (forceRefresh && _cachedKeys is not null &&
                DateTimeOffset.UtcNow - _lastRefresh < _options.KeyRotationCooldown)
                return _cachedKeys;

            var fetched = await FetchKeysFromJwksAsync(ct).ConfigureAwait(false);
            _cachedKeys = fetched;
            _cacheExpiry = DateTimeOffset.UtcNow.Add(_options.JwksCacheDuration);
            _lastRefresh = DateTimeOffset.UtcNow;
            return fetched;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async ValueTask<IReadOnlyList<SecurityKey>> FetchKeysFromJwksAsync(CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient(nameof(SupabaseJwtValidator));
        var json = await client.GetStringAsync(_jwksUri, ct).ConfigureAwait(false);
        var keySet = new JsonWebKeySet(json);
        return keySet.GetSigningKeys().ToList().AsReadOnly();
    }

    private bool ShouldAttemptKeyRotationRefresh()
    {
        return DateTimeOffset.UtcNow - _lastRefresh >= _options.KeyRotationCooldown;
    }

    private static string? SafeFragment(string token) =>
        token.Length >= 8 ? token[..8] : token.Length > 0 ? token : null;

    /// <inheritdoc />
    public void Dispose() => _refreshLock.Dispose();
}
