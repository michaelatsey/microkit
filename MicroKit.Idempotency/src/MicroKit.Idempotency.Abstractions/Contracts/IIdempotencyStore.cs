using MicroKit.Idempotency.Abstractions.Models;

namespace MicroKit.Idempotency.Abstractions.Contracts;

/// <summary>Persistence contract for reading and writing idempotency records.</summary>
public interface IIdempotencyStore
{
    /// <summary>Retrieves an idempotency state by its key.</summary>
    /// <param name="key">The idempotency key to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored <see cref="IdempotencyState"/>, or <see langword="null"/> if not found.</returns>
    Task<IdempotencyState?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates a new idempotency record with an optional time-to-live.</summary>
    /// <param name="state">The initial state to persist.</param>
    /// <param name="ttl">Optional expiry duration; <see langword="null"/> means no expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(IdempotencyState state, TimeSpan? ttl, CancellationToken cancellationToken = default);

    /// <summary>Marks an in-progress record as successfully completed and stores the response.</summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="response">The serialized response to cache for future duplicate requests.</param>
    /// <param name="status">The terminal status to assign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAsync(string key, string response, IdempotencyStatus status, CancellationToken cancellationToken = default);

    /// <summary>Marks an in-progress record as failed.</summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="status">The failure status to assign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FailAsync(string key, IdempotencyStatus status, CancellationToken cancellationToken = default);

    /// <summary>Extends the expiry of an existing idempotency record.</summary>
    /// <param name="key">The idempotency key whose expiry to renew.</param>
    /// <param name="ttl">The new time-to-live from now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RenewExpirationAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes an idempotency record.</summary>
    /// <param name="key">The idempotency key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
