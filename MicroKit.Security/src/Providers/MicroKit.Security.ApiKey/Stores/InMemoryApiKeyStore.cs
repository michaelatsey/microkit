
using System.Collections.Concurrent;
using MicroKit.Security.ApiKey.Models;

namespace MicroKit.Security.ApiKey.Stores;
/// <summary>
/// In-memory implementation of API key store.
/// Useful for testing and development.
/// </summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<string, ApiKeyRecord> _keysById = new();
    private readonly ConcurrentDictionary<string, string> _hashToId = new();

    /// <inheritdoc />
    public ValueTask<ApiKeyRecord?> GetByHashedKeyAsync(
        string hashedKey,
        CancellationToken cancellationToken = default)
    {
        if (_hashToId.TryGetValue(hashedKey, out var id) &&
            _keysById.TryGetValue(id, out var record))
        {
            return ValueTask.FromResult<ApiKeyRecord?>(record);
        }

        return ValueTask.FromResult<ApiKeyRecord?>(null);
    }

    /// <inheritdoc />
    public ValueTask<ApiKeyRecord?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            _keysById.TryGetValue(id, out var record) ? record : null);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ApiKeyRecord>> GetByOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var records = _keysById.Values
            .Where(r => r.OwnerId == ownerId)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<ApiKeyRecord>>(records);
    }

    /// <inheritdoc />
    public ValueTask<ApiKeyRecord> CreateAsync(
        ApiKeyRecord record,
        CancellationToken cancellationToken = default)
    {
        if (!_keysById.TryAdd(record.Id, record))
        {
            throw new InvalidOperationException($"API key with ID '{record.Id}' already exists");
        }

        _hashToId[record.HashedKey] = record.Id;
        return ValueTask.FromResult(record);
    }

    /// <inheritdoc />
    public ValueTask<ApiKeyRecord> UpdateAsync(
        ApiKeyRecord record,
        CancellationToken cancellationToken = default)
    {
        if (!_keysById.ContainsKey(record.Id))
        {
            throw new InvalidOperationException($"API key with ID '{record.Id}' not found");
        }

        _keysById[record.Id] = record;
        _hashToId[record.HashedKey] = record.Id;

        return ValueTask.FromResult(record);
    }

    /// <inheritdoc />
    public ValueTask RevokeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (_keysById.TryGetValue(id, out var record))
        {
            var revoked = record with { IsActive = false };
            _keysById[id] = revoked;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UpdateLastUsedAsync(
        string id,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        if (_keysById.TryGetValue(id, out var record))
        {
            var updated = record with { LastUsedAt = timestamp };
            _keysById[id] = updated;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears all stored keys. For testing purposes.
    /// </summary>
    public void Clear()
    {
        _keysById.Clear();
        _hashToId.Clear();
    }
}
