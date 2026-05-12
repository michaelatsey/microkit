using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>
/// Réprésente un service de nettoyage de la boîte d'envoi (Outbox) du système de messagerie,
/// responsable de la suppression des messages en attente qui sont plus anciens qu'une certaine date 
/// et qui ont un certain statut, permettant de maintenir la boîte d'envoi propre 
/// et efficace en supprimant les messages obsolètes ou traités, 
/// en respectant les paramètres de configuration définis pour le nettoyage de la boîte d'envoi, 
/// tels que la taille des lots et les critères de sélection des messages à supprimer.
/// </summary>
public interface IOutboxCleanupService
{
    /// <summary>
    /// Supprime les messages de la boîte d'envoi (Outbox) qui sont plus anciens que la date spécifiée
    /// </summary>
    /// <param name="olderThan">Only messages older than this timestamp are eligible for deletion.</param>
    /// <param name="status">The message status that entries must have to be eligible.</param>
    /// <param name="batchSize">Maximum number of messages to delete per invocation.</param>
    /// <param name="tenantId">Optional tenant identifier to scope the cleanup; pass <see langword="null"/> to clean all tenants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Le nombre de messages supprimés de la boîte d'envoi (Outbox) qui sont plus anciens que la date spécifiée 
    /// et qui ont le statut spécifié.
    /// </returns>
    Task<int> CleanupAsync(
        DateTimeOffset olderThan,
        MessageStatus status,
        int batchSize,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
