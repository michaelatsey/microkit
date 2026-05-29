using MicroKit.MediatR.Behaviors.Pipeline;

namespace MicroKit.MediatR.Behaviors.Idempotency;

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
    /// Retrieves a previously stored response for the given key,
    /// or <see langword="null"/> if no entry exists.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="key">The idempotency key from <see cref="IIdempotentCommand.IdempotencyKey"/>.</param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    /// <returns>The stored response, or <see langword="null"/> on a cache miss.</returns>
    ValueTask<TResponse?> GetAsync<TResponse>(string key, CancellationToken ct = default);

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
