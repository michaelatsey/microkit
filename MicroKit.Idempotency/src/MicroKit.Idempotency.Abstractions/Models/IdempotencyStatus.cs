namespace MicroKit.Idempotency.Abstractions.Models;

/// <summary>Represents the lifecycle state of an idempotency record from creation to terminal outcome.</summary>
public enum IdempotencyStatus
{
    /// <summary>
    /// L'opération n'a pas encore été traitée, et le résultat de la demande n'est pas encore disponible. Le statut "Pending" indique que l'opération est en attente de traitement, et que le résultat de la demande n'est pas encore prêt à être utilisé par les clients ou les consommateurs de l'API.
    /// </summary>
    Processing = 1,
    /// <summary>
    /// L'opération a été complétée avec succès, et le résultat de la demande est disponible. Le statut "Completed" indique que l'opération a été traitée sans erreur, et que le résultat de la demande peut être utilisé en toute confiance par les clients ou les consommateurs de l'API.
    /// </summary>
    Completed = 2,
    /// <summary>
    /// L'opération a échoué, généralement en raison d'une exception ou d'une erreur lors du traitement de la demande. Le statut "Failed" indique que l'opération n'a pas pu être complétée avec succès, et que le résultat de la demande est considéré comme invalide ou non fiable.
    /// </summary>
    Failed = 3,
    /// <summary>
    /// L'opération a été annulée, généralement en raison d'une expiration ou d'une suppression de la clé d'idempotence avant l'achèvement de l'opération.
    /// </summary>
    Cancelled = 4

}
