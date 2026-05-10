namespace MicroKit.Idempotency.Core.Configuration;

/// <summary>
/// Réprésente les options de configuration pour la boîte d'envoi (Idempotency) du système de messagerie, permettant de définir les paramètres liés à l'envoi et à la gestion des messages à envoyer.
/// </summary>
public sealed class IdempotencyOptions
{
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(24);
    public bool VerifyRequestHashes { get; set; } = true;
    public bool EnableLogging { get; set; } = true;

    // Multi-tenancy options
    public bool IsMultiTenant { get; set; } = false;

    // Cleanup options
    public TimeSpan? CleanupRunInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan FailedRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    public int CleanupBatchSize { get; set; } = 1000;

    ///// <summary>
    ///// Gets the tenant identifier provider.
    ///// TODO: utiliser un contexte de tenant plutôt que d'avoir une fonction de résolution dans les options, cela permettra d'avoir une meilleure séparation des préoccupations et de rendre le code plus modulaire et testable.En utilisant un contexte de tenant, on peut encapsuler les informations spécifiques au tenant dans une classe dédiée, ce qui facilite la gestion des données liées au tenant et améliore la maintenabilité du code.
    ///// </summary>
    ///// <value>
    ///// The tenant identifier provider.
    ///// </value>


}
