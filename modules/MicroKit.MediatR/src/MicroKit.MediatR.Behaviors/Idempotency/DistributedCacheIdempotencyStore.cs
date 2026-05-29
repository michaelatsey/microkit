using MicroKit.MediatR.Behaviors.Idempotency;

namespace MicroKit.MediatR.Behaviors;

/// <summary>
/// Default <see cref="IIdempotencyStore"/> implementation backed by
/// <see cref="IDistributedCache"/> and <c>System.Text.Json</c>.
/// </summary>
/// <remarks>
/// For <c>Result&lt;T&gt;</c> responses, register <c>ResultJsonConverterFactory</c> in
/// <c>IOptions&lt;JsonSerializerOptions&gt;</c> (see ADR-007).
/// </remarks>
/// <param name="cache">The distributed cache for storing serialized responses.</param>
/// <param name="jsonOptions">The JSON serializer options, including any custom converters.</param>
public sealed class DistributedCacheIdempotencyStore(
    IDistributedCache cache,
    IOptions<JsonSerializerOptions> jsonOptions) : IIdempotencyStore
{
    private static readonly DistributedCacheEntryOptions _defaultOptions = new();

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TResponse is a generic parameter preserved by the open-generic behavior registration via [RequiresDynamicCode] callers.")]
    public async ValueTask<TResponse?> GetAsync<TResponse>(string key, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(key, ct).ConfigureAwait(false);
        if (bytes is null)
            return default;

        return JsonSerializer.Deserialize<TResponse>(bytes, jsonOptions.Value);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TResponse is a generic parameter preserved by the open-generic behavior registration via [RequiresDynamicCode] callers.")]
    public async ValueTask SetAsync<TResponse>(string key, TResponse response, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, jsonOptions.Value);
        await cache.SetAsync(key, bytes, _defaultOptions, ct).ConfigureAwait(false);
    }
}
