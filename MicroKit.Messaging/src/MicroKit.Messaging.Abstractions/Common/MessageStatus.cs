namespace MicroKit.Messaging.Abstractions.Common;

/// <summary>
/// Réprésente les différents statuts possibles d'un message dans le contexte de la messagerie, permettant de suivre l'état de traitement d'un message à travers son cycle de vie, depuis sa création jusqu'à sa consommation ou son échec.
/// </summary>
public enum MessageStatus
{
    /// <summary>
    /// The pending (commun)
    /// </summary>
    Pending,      // En attente de traitement
    /// <summary>
    /// The processing (commun)
    /// </summary>
    Processing,   // En cours de traitement
    /// <summary>
    /// The published (outbox)
    /// </summary>
    Published,    // Publié avec succès
    /// <summary>
    /// The consumed (inbox)
    /// </summary>
    Consumed,     // Consommé avec succès
    /// <summary>
    /// The failed (commun)
    /// </summary>
    Failed,  // Échec après tous les retry    
    /// <summary>
    /// The dead lettered
    /// </summary>
    DeadLettered

}
