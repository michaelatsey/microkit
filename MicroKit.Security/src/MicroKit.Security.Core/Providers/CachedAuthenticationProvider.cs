namespace MicroKit.Security.Core.Providers;

using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Abstractions.Identity;
using MicroKit.Security.Abstractions.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Two-level (L1 memory + L2 distributed) caching decorator for an <see cref="IAuthenticationProvider"/>.
/// Implements SingleFlight to prevent cache stampedes under bursts of concurrent requests.
/// </summary>
public sealed class CachedAuthenticationProvider(
    IAuthenticationProvider innerProvider,
    IMemoryCache memoryCache,
    IDistributedCache distributedCache,
    IOptions<CacheOptions> options,
    ILogger<CachedAuthenticationProvider> logger) : IAuthenticationProvider
{
    private const byte SerializationVersion = 1;
    private readonly CacheOptions _cacheOptions = options.Value;

    // SingleFlight: prevents multiple concurrent requests from hitting the backing store simultaneously.
    private static readonly ConcurrentDictionary<string, Lazy<Task<AuthenticationResult>>> _inflight = new();

    /// <inheritdoc />
    public AuthenticationScheme Scheme => innerProvider.Scheme;

    /// <inheritdoc />
    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        ReadOnlyMemory<char> credentials,
        CancellationToken cancellationToken = default)
    {
        if (!_cacheOptions.Enabled)
            return await innerProvider.AuthenticateAsync(credentials, cancellationToken);

        var cacheKey = GenerateCacheKey(credentials.Span);

        // L1: memory cache
        if (memoryCache.TryGetValue(cacheKey, out AuthenticationResult? memoryResult))
        {
            logger.LogDebug("L1 cache hit for {Scheme}", Scheme);
            return memoryResult! with
            {
                Metadata = Enrich(memoryResult.Metadata, "IdentityStore_DB")
            };
        }

        // L2: distributed cache, also guarded by SingleFlight to avoid Redis saturation on spikes.
        var lazyTask = _inflight.GetOrAdd(cacheKey, _ =>
            new Lazy<Task<AuthenticationResult>>(() => ExecuteFullResolutionAsync(cacheKey, credentials, cancellationToken)));

        try
        {
            return await lazyTask.Value;
        }
        finally
        {
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    private async Task<AuthenticationResult> ExecuteFullResolutionAsync(
        string cacheKey,
        ReadOnlyMemory<char> credentials,
        CancellationToken ct)
    {
        // 1. Try L2
        try
        {
            var cachedBytes = await distributedCache.GetAsync(cacheKey, ct);
            if (cachedBytes is not null)
            {
                var result = DeserializeResult(cachedBytes);
                if (result != null)
                {
                    logger.LogDebug("L2 cache hit for {Scheme}", Scheme);
                    SetMemoryCache(cacheKey, result);
                    return result with { Metadata = Enrich(result.Metadata, "DistributedCache_L2") };
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "L2 cache read failed for {Scheme}", Scheme);
        }

        // 2. Call the real provider (source of truth)
        var freshResult = await innerProvider.AuthenticateAsync(credentials, ct);

        // 3. Cache if eligible
        if (ShouldCache(freshResult))
        {
            await CacheResultAsync(cacheKey, freshResult, ct);
        }

        return freshResult with { Metadata = Enrich(freshResult.Metadata!, "IdentityStore_RealTime") };
    }

    private void SetMemoryCache(string key, AuthenticationResult result)
    {
        // L1 lifetime is capped at 50% of the L2 duration (max 5 min) to limit memory pressure on the web server.
        var totalSeconds = result.IsSuccess
            ? _cacheOptions.SuccessDurationSeconds
            : _cacheOptions.FailureDurationSeconds;

        var l1Seconds = Math.Min(totalSeconds / 2, 300);

        if (l1Seconds > 0)
        {
            memoryCache.Set(key, result, TimeSpan.FromSeconds(l1Seconds));
        }
    }

    private async Task CacheResultAsync(string key, AuthenticationResult result, CancellationToken ct)
    {
        try
        {
            var duration = result.IsSuccess
                ? TimeSpan.FromSeconds(_cacheOptions.SuccessDurationSeconds)
                : TimeSpan.FromSeconds(_cacheOptions.FailureDurationSeconds);

            var bytes = SerializeResult(result);
            await distributedCache.SetAsync(key, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration
            }, ct);

            SetMemoryCache(key, result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for {Scheme}", Scheme);
        }
    }

    private bool ShouldCache(AuthenticationResult result)
    {
        if (!result.IsSuccess && result.Status == ValidationStatus.Expired)
            return false;

        return result.IsSuccess || _cacheOptions.FailureDurationSeconds > 0;
    }

    private string GenerateCacheKey(ReadOnlySpan<char> credentials)
    {
        var byteCount = Encoding.UTF8.GetByteCount(credentials);
        byte[]? pooled = null;
        Span<byte> buffer = byteCount <= 1024
            ? stackalloc byte[byteCount]
            : (pooled = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            Encoding.UTF8.GetBytes(credentials, buffer);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(buffer[..byteCount], hash);

            var prefix = string.IsNullOrWhiteSpace(_cacheOptions.KeyPrefix) ? "auth_" : _cacheOptions.KeyPrefix;
            return $"{prefix}{Scheme}:{Convert.ToHexString(hash)}";
        }
        finally
        {
            if (pooled != null) ArrayPool<byte>.Shared.Return(pooled);
        }
    }

    private static byte[] SerializeResult(AuthenticationResult result)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, true);

        writer.Write(SerializationVersion);
        writer.Write(result.IsSuccess);
        writer.Write((byte)result.Status);
        writer.Write(result.ErrorMessage ?? string.Empty);

        if (result.IsSuccess && result.Principal != null)
        {
            writer.Write(result.Principal.Identifier ?? string.Empty);
            writer.Write(result.Principal.DisplayName ?? string.Empty);
            writer.Write(result.Principal.TenantId ?? string.Empty);

            var claims = result.Principal.Claims;
            if (claims is ICollection<SecurityClaim> coll)
            {
                writer.Write(coll.Count);
            }
            else
            {
                var list = claims.ToList();
                writer.Write(list.Count);
                claims = list;
            }

            foreach (var claim in claims)
            {
                writer.Write(claim.Type);
                writer.Write(claim.Value);
            }
        }
        return ms.ToArray();
    }

    private static AuthenticationResult? DeserializeResult(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            if (reader.ReadByte() != SerializationVersion) return null;

            var isSuccess = reader.ReadBoolean();
            var status = (ValidationStatus)reader.ReadByte();
            var error = reader.ReadString();

            if (!isSuccess)
                return AuthenticationResult.Failure(status, string.IsNullOrEmpty(error) ? null : error);

            var principal = new SecurityPrincipal(
                Identifier: reader.ReadString(),
                DisplayName: reader.ReadString(),
                TenantId: reader.ReadString(),
                Claims: ReadClaims(reader));

            return AuthenticationResult.Success(principal);
        }
        catch { return null; }
    }

    private static List<SecurityClaim> ReadClaims(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var list = new List<SecurityClaim>(count);
        for (int i = 0; i < count; i++)
            list.Add(new SecurityClaim(reader.ReadString(), reader.ReadString()));
        return list;
    }

    private IReadOnlyDictionary<string, object> Enrich(IReadOnlyDictionary<string, object>? original, string source)
    {
        // auth_source is never stored in cache; it is derived here to reflect the actual data origin.
        var copy = new Dictionary<string, object>(original ?? new Dictionary<string, object>())
        {
            ["auth_source"] = source,
            ["cache_enabled"] = _cacheOptions.Enabled,
            ["authenticated_at"] = DateTimeOffset.UtcNow
        };
        return copy;
    }
}
