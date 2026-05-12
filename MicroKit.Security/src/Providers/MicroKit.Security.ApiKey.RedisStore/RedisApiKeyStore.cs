using MicroKit.Security.ApiKey.Stores;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using MicroKit.Security.ApiKey.Models;

namespace MicroKit.Security.ApiKey.RedisStore;

/// <summary>Redis-backed implementation of <see cref="IApiKeyStore"/> using StackExchange.Redis.</summary>
public sealed class RedisApiKeyStore : IApiKeyStore
{
    private readonly IDatabase _db;

    private static class RedisKeys
    {
        public static string Key(string id) => $"security:apikey:{id}";
        public static string Lookup() => "security:apikey:lookup";
        public static string OwnerIndex(string ownerId) => $"security:apikey:owner:{ownerId}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    /// <summary>Initializes a new instance.</summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    public RedisApiKeyStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    /// <summary>
    /// Gets an API key record by its ID.
    /// </summary>
    /// <param name="id">API key ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// API key record if found.
    /// </returns>
    public async ValueTask<ApiKeyRecord?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var data = await _db.StringGetAsync(RedisKeys.Key(id));
        if (data.IsNull) return null;

        // Utilisation de ReadOnlyMemory pour éviter les allocations de strings inutiles
        return JsonSerializer.Deserialize<ApiKeyRecord>((byte[])data!, JsonOptions);
    }

    /// <summary>
    /// Gets an API key record by its hashed value.
    /// </summary>
    /// <param name="hashedKey">Hashed API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// API key record if found.
    /// </returns>
    public async ValueTask<ApiKeyRecord?> GetByHashedKeyAsync(
        string hashedKey,
        CancellationToken cancellationToken = default)
    {
        // 1. Chercher l'ID correspondant au hash dans l'index secondaire
        var id = await _db.HashGetAsync(RedisKeys.Lookup(), hashedKey);

        return id.HasValue ? await GetByIdAsync(id!, cancellationToken) : null;
    }

    /// <summary>
    /// Creates a new API key record.
    /// </summary>
    /// <param name="record">API key record to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Created API key record.
    /// </returns>
    /// <exception cref="InvalidOperationException">Failed to persist API Key to Redis.</exception>
    public async ValueTask<ApiKeyRecord> CreateAsync(
        ApiKeyRecord record,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);

        // Utilisation d'une transaction Redis (Multi/Exec) pour garantir l'intégrité
        var tran = _db.CreateTransaction();

        // Ajout du record + Index de Hash + Index par Owner
        _ = tran.StringSetAsync(RedisKeys.Key(record.Id), json);
        _ = tran.HashSetAsync(RedisKeys.Lookup(), record.HashedKey, record.Id);
        _ = tran.SetAddAsync(RedisKeys.OwnerIndex(record.OwnerId), record.Id);

        if (!await tran.ExecuteAsync())
        {
            throw new InvalidOperationException("Failed to persist API Key to Redis.");
        }

        return record;
    }

    /// <summary>
    /// Gets all API keys for an owner.
    /// </summary>
    /// <param name="ownerId">Owner ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Collection of API key records.
    /// </returns>
    public async ValueTask<IReadOnlyList<ApiKeyRecord>> GetByOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        // 1. Récupérer tous les IDs liés à ce propriétaire depuis le SET
        var ids = await _db.SetMembersAsync(RedisKeys.OwnerIndex(ownerId));
        if (ids.Length == 0) return [];

        // 2. Batching pour la performance : Récupérer toutes les clefs en un seul appel (MGET)
        var keys = Array.ConvertAll(ids, id => (RedisKey)RedisKeys.Key(id!));
        var results = await _db.StringGetAsync(keys);

        var list = new List<ApiKeyRecord>(results.Length);
        foreach (var value in results)
        {
            if (value.HasValue)
            {
                var record = JsonSerializer.Deserialize<ApiKeyRecord>((byte[])value!, JsonOptions);
                if (record != null) list.Add(record);
            }
        }

        return list;
    }

    /// <summary>
    /// Updates the last used timestamp.
    /// </summary>
    /// <param name="id">API key ID.</param>
    /// <param name="timestamp">Usage timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns></returns>
    public async ValueTask UpdateLastUsedAsync(
        string id,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        // On récupère, on modifie, on sauvegarde
        // Note: Dans un système ultra-haute charge, on pourrait utiliser un Lua script
        // pour ne pas désérialiser/resérialiser tout l'objet.
        //var record = await GetByIdAsync(id, cancellationToken);
        //if (record is not null)
        //{
        //    var updated = record with { LastUsedAt = timestamp };
        //    await _db.StringSetAsync($"{_baseKey}{id}", JsonSerializer.Serialize(updated, JsonOptions));
        //}

        // Approche "Expert" : On utilise un script Lua pour mettre à jour un champ JSON
        // sans rapatrier tout l'objet sur le réseau (réduction CPU/Bande passante)
        const string luaScript = @"
            local val = redis.call('GET', KEYS[1])
            if val then
                local obj = cjson.decode(val)
                obj['LastUsedAt'] = ARGV[1]
                redis.call('SET', KEYS[1], cjson.encode(obj))
                return 1
            end
            return 0";

        await _db.ScriptEvaluateAsync(luaScript,
            [RedisKeys.Key(id)],
            [timestamp.ToString("O")]);

    }

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    /// <param name="id">API key ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns></returns>
    public async ValueTask RevokeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var record = await GetByIdAsync(id, cancellationToken);
        if (record is not null)
        {
            await CreateAsync(record with { IsActive = false }, cancellationToken);
        }
    }

    /// <summary>
    /// Updates an existing API key record.
    /// </summary>
    /// <param name="record">API key record to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Updated API key record.
    /// </returns>
    public async ValueTask<ApiKeyRecord> UpdateAsync(ApiKeyRecord record, CancellationToken cancellationToken = default) 
        => await CreateAsync(record, cancellationToken);
}
