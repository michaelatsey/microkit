namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>
/// Réprésente un processeur de boîte d'envoi (Outbox) responsable du traitement des messages en attente d'envoi, permettant de gérer l'envoi des messages de manière fiable et efficace, en respectant les paramètres de configuration définis pour la boîte d'envoi, tels que la taille des lots, les intervalles de sondage et les stratégies de réessai.
/// </summary>
public interface IOutboxProcessor
{
    /// <summary>
    /// Processes the batch asynchronous.
    /// </summary>
    /// <param name="batchSize">Size of the batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    Task ProcessBatchAsync(
        string tenantId,
        int batchSize,
        CancellationToken cancellationToken = default);
}
