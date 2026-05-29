using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.Behaviors.Idempotency;

/// <summary>
/// Discriminated hit wrapper returned by <see cref="IIdempotencyStore.GetAsync{TResponse}"/>.
/// A non-<see langword="null"/> entry is always a cache hit; <see langword="null"/> is always a miss,
/// regardless of whether <typeparamref name="TResponse"/> is a reference type, value struct, or
/// <c>Result&lt;T&gt;</c>. This avoids the ambiguity of <c>default(TResponse)</c> as a sentinel,
/// which is unsafe for struct TResponse types including <c>Result&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="TResponse">The cached response type.</typeparam>
/// <param name="Value">The cached response value.</param>
public sealed record CacheEntry<TResponse>(TResponse Value);

/// <summary>
/// Stores and retrieves idempotent command responses by key.
/// Used by <see cref="IdempotencyBehavior{TRequest,TResponse}"/> (pipeline order 400).
/// </summary>
/// <remarks>
/// Register an implementation in DI — the default
/// <see cref="DistributedCacheIdempotencyStore"/> uses <c>IDistributedCache</c> with STJ.
/// Custom implementations may use any storage and encoding strategy.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Retrieves a previously stored response for the given key.
    /// Returns a non-null <see cref="CacheEntry{TResponse}"/> on a hit,
    /// or <see langword="null"/> on a cache miss. The <see langword="null"/>/<see langword="not null"/>
    /// distinction is unambiguous for every <typeparamref name="TResponse"/> type, including
    /// value structs and <c>Result&lt;T&gt;</c> whose <c>default</c> state is not a valid cached value.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="key">The idempotency key from <see cref="IIdempotentCommand.IdempotencyKey"/>.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>A <see cref="CacheEntry{TResponse}"/> on hit; <see langword="null"/> on miss.</returns>
    ValueTask<CacheEntry<TResponse>?> GetAsync<TResponse>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores a response under the given key.
    /// Called only when the handler succeeds — never on <c>Result.Failure</c>.
    /// </summary>
    /// <typeparam name="TResponse">The response type to store.</typeparam>
    /// <param name="key">The idempotency key from <see cref="IIdempotentCommand.IdempotencyKey"/>.</param>
    /// <param name="response">The response value to persist.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask SetAsync<TResponse>(string key, TResponse response, CancellationToken ct = default);
}
