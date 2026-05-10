namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>
/// Réprésente le processeur de la boîte de réception (Inbox) pour le traitement des messages reçus, en assurant l'idempotence et la gestion des erreurs.
/// </summary>
public interface IInboxProcessor
{
    Task ProcessBatchAsync(
        string tenantId,
        string consumerName, 
        int batchSize, 
        CancellationToken cancellationToken = default);
}
