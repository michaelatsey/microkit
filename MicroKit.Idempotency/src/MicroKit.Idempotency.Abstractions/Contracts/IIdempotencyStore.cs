using MicroKit.Idempotency.Abstractions.Models;

namespace MicroKit.Idempotency.Abstractions.Contracts;

public interface IIdempotencyStore
{
    Task<IdempotencyState?> GetAsync(string key, CancellationToken cancellationToken = default);


    Task CreateAsync(IdempotencyState state, TimeSpan? ttl, CancellationToken cancellationToken = default);

    Task CompleteAsync(string key, string response, IdempotencyStatus status, CancellationToken cancellationToken = default);
    Task FailAsync(string key, IdempotencyStatus status, CancellationToken cancellationToken = default);

    Task RenewExpirationAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

}
