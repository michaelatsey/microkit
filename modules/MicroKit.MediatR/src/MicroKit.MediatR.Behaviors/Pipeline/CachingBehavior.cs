using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.Behaviors;

/// <summary>
/// Serves <see cref="ICacheableQuery"/> results from <see cref="IDistributedCache"/>
/// before the handler executes. Never caches a <c>Result.Failure</c> response.
/// Logs a WARNING when <see cref="ICacheableQuery.Expiry"/> is <see langword="null"/>.
/// Queries only (commands and stream queries pass through).
/// Pipeline order: <see cref="PipelineOrder.Caching"/> (500).
/// </summary>
/// <remarks>
/// For <c>Result&lt;T&gt;</c> responses, register <c>ResultJsonConverterFactory</c> in
/// <c>IOptions&lt;JsonSerializerOptions&gt;</c> (ADR-007). Without it, STJ cannot round-trip
/// <c>Result&lt;T&gt;</c> values (readonly struct with private constructors).
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed partial class CachingBehavior<TRequest, TResponse>(
    IDistributedCache cache,
    IOptions<JsonSerializerOptions> jsonOptions,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string _requestName = typeof(TRequest).Name;
    private static readonly string _responseTypeName = typeof(TResponse).FullName ?? typeof(TResponse).Name;

    /// <inheritdoc />
    public override int Order => PipelineOrder.Caching;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TResponse is a generic parameter preserved by the open-generic behavior registration via [RequiresDynamicCode] callers.")]
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery cacheableQuery)
            return await next().ConfigureAwait(false);

        if (cacheableQuery.Expiry is null)
            LogNullExpiry(logger, _requestName);

        var bytes = await cache.GetAsync(cacheableQuery.CacheKey, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<TResponse>(bytes, jsonOptions.Value)!;
            }
            catch (JsonException ex)
            {
                LogDeserializationFailure(logger, _requestName, cacheableQuery.CacheKey, ex);
                return CreateFailureOrThrow(
                    new CacheDeserializationError(cacheableQuery.CacheKey, _responseTypeName),
                    new InvalidOperationException(
                        $"CachingBehavior: failed to deserialize cached entry for key '{cacheableQuery.CacheKey}' " +
                        $"into '{_responseTypeName}'. See inner exception for details.", ex));
            }
        }

        var response = await next().ConfigureAwait(false);

        // Never cache a failure — a failed query result is not safe to replay.
        if (!ResultInspector<TResponse>.IsFailure(response))
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(response, jsonOptions.Value);
            var options = cacheableQuery.Expiry.HasValue
                ? new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = cacheableQuery.Expiry }
                : new DistributedCacheEntryOptions();

            await cache.SetAsync(cacheableQuery.CacheKey, serialized, options, cancellationToken)
                       .ConfigureAwait(false);
        }

        return response;
    }

    [LoggerMessage(2000, LogLevel.Warning,
        "CachingBehavior: {CommandName} has a null Expiry — cached entry will not expire automatically")]
    private static partial void LogNullExpiry(ILogger logger, string commandName);

    [LoggerMessage(2001, LogLevel.Error,
        "CachingBehavior: {CommandName} — deserialization of cached entry for key '{CacheKey}' failed")]
    private static partial void LogDeserializationFailure(ILogger logger, string commandName, string cacheKey, Exception ex);
}
