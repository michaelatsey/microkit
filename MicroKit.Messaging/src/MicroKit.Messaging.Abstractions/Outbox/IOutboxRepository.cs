using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>
/// Représente le référentiel de la boîte d'envoi (Outbox) pour l'accès et la gestion des messages à envoyer.
/// </summary>
public interface IOutboxRepository
{
    /// <summary>
    /// Récupère un message par son identifiant unique.
    /// </summary>
    Task<OutboxMessage?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute un nouveau message à l'Outbox (généralement appelé par le OutboxManager).
    /// </summary>
    Task<string> AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute une collection de messages en une seule opération.
    /// </summary>
    Task AddRangeAsync(IReadOnlyCollection<OutboxMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verrouille atomiquement un batch de messages pour un tenant spécifique.
    /// Utilise la stratégie Skip Locked selon le moteur de base de données.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> LockNextBatchAsync(
        string tenantId, 
        int batchSize, 
        TimeSpan lockDuration, 
        CancellationToken ct = default);

    /// <summary>
    /// Met à jour l'état d'un message (ex: passage à Processed après envoi réussi).
    /// </summary>
    Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime définitivement un message.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Compte le nombre de messages en attente pour un tenant.
    /// </summary>
    Task<long> GetPendingCountAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Marque un message comme définitivement échoué après épuisement des tentatives.
    /// </summary>
    Task MarkAsFailedAsync(string messageId, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Réinitialise les messages bloqués au statut Processing dont le bail a expiré.
    /// </summary>
    Task ResetStuckProcessingMessagesAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nettoie les anciens messages traités ou échoués pour libérer de l'espace.
    /// </summary>
    Task<int> CleanupAsync(
        DateTimeOffset olderThan, 
        MessageStatus status, 
        int batchSize, 
        CancellationToken cancellationToken = default);
}
