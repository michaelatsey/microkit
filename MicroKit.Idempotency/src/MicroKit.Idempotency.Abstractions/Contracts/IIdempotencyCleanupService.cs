using MicroKit.Idempotency.Abstractions.Models;

namespace MicroKit.Idempotency.Abstractions.Contracts;

/// <summary>
/// Réprésente un service de nettoyage de la boîte d'envoi (Outbox) du système de messagerie,
/// responsable de la suppression des messages en attente qui sont plus anciens qu'une certaine date 
/// et qui ont un certain statut, permettant de maintenir la boîte d'envoi propre 
/// et efficace en supprimant les messages obsolètes ou traités, 
/// en respectant les paramètres de configuration définis pour le nettoyage de la boîte d'envoi, 
/// tels que la taille des lots et les critères de sélection des messages à supprimer.
/// </summary>
public interface IIdempotencyCleanupService
{
    /// <summary>
    /// Réprésente un service de nettoyage de table d'idempotence, responsable de la suppression des entrées 
    /// d'idempotence qui sont plus anciennes qu'une certaine date
    /// </summary>
    /// <param name="olderThan">The older than.</param>
    /// <param name="status">The status.</param>
    /// <param name="batchSize">Size of the batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task<int> CleanupAsync(
        DateTimeOffset olderThan,
        IdempotencyStatus status,
        int batchSize,
        CancellationToken cancellationToken = default);
}
