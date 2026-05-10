
using MicroKit.Security.ApiKey.Models;

namespace MicroKit.Security.ApiKey.Stores;
/// <summary>
/// Interface for API key storage operations.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>
    /// Gets an API key record by its hashed value.
    /// </summary>
    /// <param name="hashedKey">Hashed API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>API key record if found.</returns>
    ValueTask<ApiKeyRecord?> GetByHashedKeyAsync(
        string hashedKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an API key record by its ID.
    /// </summary>
    /// <param name="id">API key ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>API key record if found.</returns>
    ValueTask<ApiKeyRecord?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all API keys for an owner.
    /// </summary>
    /// <param name="ownerId">Owner ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of API key records.</returns>
    ValueTask<IReadOnlyList<ApiKeyRecord>> GetByOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new API key record.
    /// </summary>
    /// <param name="record">API key record to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created API key record.</returns>
    ValueTask<ApiKeyRecord> CreateAsync(
        ApiKeyRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing API key record.
    /// </summary>
    /// <param name="record">API key record to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated API key record.</returns>
    ValueTask<ApiKeyRecord> UpdateAsync(
        ApiKeyRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    /// <param name="id">API key ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RevokeAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last used timestamp.
    /// </summary>
    /// <param name="id">API key ID.</param>
    /// <param name="timestamp">Usage timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask UpdateLastUsedAsync(
        string id,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);
}
