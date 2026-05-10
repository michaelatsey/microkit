using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using System.Collections.Concurrent;

namespace MicroKit.Idempotency.Core.Persistence;

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private static readonly ConcurrentDictionary<string, CacheEntry> _store = new();

    private record CacheEntry(IdempotencyState State, DateTimeOffset? ExpiresAt);


    public Task CompleteAsync(string key, string response, IdempotencyStatus status, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            var updatedState = entry.State with
            {
                Status = status,
                Response = response,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };

            _store.TryUpdate(key, new CacheEntry(updatedState, entry.ExpiresAt), entry);
        }

        return Task.CompletedTask;
    }

    public Task CreateAsync(IdempotencyState state, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        var expiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : (DateTimeOffset?)null;
        var entry = new CacheEntry(state, expiresAt);

        if (!_store.TryAdd(state.Key, entry))
        {
            throw new InvalidOperationException($"Idempotency key '{state.Key}' already exists");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task FailAsync(string key, IdempotencyStatus status, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            var failedState = entry.State with { Status = status, CompletedAtUtc = DateTimeOffset.UtcNow };
            _store.TryUpdate(key, entry with { State = failedState }, entry);
        }
        return Task.CompletedTask;

    }

    public Task<IdempotencyState?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                _store.TryRemove(key, out _);
                return Task.FromResult<IdempotencyState?>(null);
            }

            return Task.FromResult<IdempotencyState?>(entry.State);
        }

        return Task.FromResult<IdempotencyState?>(null);
    }

    public Task RenewExpirationAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
            _store.TryUpdate(key, entry with { ExpiresAt = expiresAt }, entry);
        }

        return Task.CompletedTask;
    }
}
