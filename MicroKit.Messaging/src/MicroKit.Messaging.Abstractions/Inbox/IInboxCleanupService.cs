using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>
/// Réprésente un service de nettoyage de la boîte d'envoi (Outbox) du système de messagerie,
/// responsable de la suppression des messages en attente qui sont plus anciens qu'une certaine date 
/// et qui ont un certain statut, permettant de maintenir la boîte d'envoi propre 
/// et efficace en supprimant les messages obsolètes ou traités, 
/// en respectant les paramètres de configuration définis pour le nettoyage de la boîte d'envoi, 
/// tels que la taille des lots et les critères de sélection des messages à supprimer.
/// </summary>
public interface IInboxCleanupService
{
    /// <summary>
    /// Supprime les messages de la boîte d'envoi (Outbox) qui sont plus anciens que la date spécifiée
    /// </summary>
    /// <param name="olderThan">The older than.</param>
    /// <param name="status">The status.</param>
    /// <param name="batchSize">Size of the batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// Le nombre de messages supprimés de la boîte d'envoi (Outbox) qui sont plus anciens que la date spécifiée 
    /// et qui ont le statut spécifié.
    /// </returns>
    Task<int> CleanupAsync(
        string tenantId,
        string consumerGroup,
        DateTimeOffset olderThan,
        MessageStatus status,
        int batchSize,
        CancellationToken cancellationToken = default);
}
