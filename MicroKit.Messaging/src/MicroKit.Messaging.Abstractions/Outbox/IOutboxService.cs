using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>
/// Represente le service de gestion de la boîte d'envoi (Outbox) pour l'enregistrement et la gestion des messages à envoyer.
/// </summary>
public interface IOutboxService
{
    /// <summary>
    /// Enqueues the asynchronous.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="causationId">The causation identifier.</param>
    /// <param name="idempotencyKey">Optional idempotency key for deduplication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated message identifier.</returns>
    Task<string> EnqueueAsync<T>(
        string tenantId,
        string messageId,
        T payload,
        OutboxDestination destination,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Enqueues the asynchronous.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="messageType">Type of the message.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="causationId">The causation identifier.</param>
    /// <param name="idempotencyKey">Optional idempotency key for deduplication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated message identifier.</returns>
    Task<string> EnqueueAsync(
        string messageId,
        string tenantId,
        string messageType,
        string payload, // eveloppe
        OutboxDestination destination,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues the batch asynchronous.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task<IEnumerable<string>> EnqueueBatchAsync(
        IEnumerable<OutboxMessage> messages,
        CancellationToken cancellationToken = default);
}
