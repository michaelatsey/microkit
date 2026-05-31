namespace MicroKit.MediatR.Behaviors.Errors;

/// <summary>
/// Produced by <see cref="CachingBehavior{TRequest,TResponse}"/> when a cached entry exists
/// but cannot be deserialized into <c>TResponse</c>.
/// </summary>
/// <remarks>
/// Common causes: cached bytes are corrupted, or the response schema changed after the entry
/// was stored (e.g., a deployment with a breaking model change). The cache entry should be
/// evicted and the handler re-invoked on the next request.
/// </remarks>
/// <param name="CacheKey">The cache key whose stored bytes could not be deserialized.</param>
/// <param name="ResponseTypeName">The fully qualified name of the target response type.</param>
public sealed record CacheDeserializationError(string CacheKey, string ResponseTypeName)
    : Error(ErrorCode.Internal, $"Failed to deserialize cached entry for key '{CacheKey}' into '{ResponseTypeName}'.")
{
    /// <inheritdoc />
    public override ErrorCategory Category => ErrorCategory.Technical;
}
