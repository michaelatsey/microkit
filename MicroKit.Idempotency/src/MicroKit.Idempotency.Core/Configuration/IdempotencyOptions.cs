namespace MicroKit.Idempotency.Core.Configuration;

/// <summary>
/// Réprésente les options de configuration pour la boîte d'envoi (Idempotency) du système de messagerie, permettant de définir les paramètres liés à l'envoi et à la gestion des messages à envoyer.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>Gets or sets how long an idempotency record is retained before it expires.</summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(24);
    /// <summary>Gets or sets whether request payloads are hashed to detect duplicate submissions with different keys.</summary>
    public bool VerifyRequestHashes { get; set; } = true;
    /// <summary>Gets or sets whether idempotency operations are logged.</summary>
    public bool EnableLogging { get; set; } = true;

    // Multi-tenancy options
    /// <summary>Gets or sets whether idempotency keys are scoped per tenant.</summary>
    public bool IsMultiTenant { get; set; } = false;

    // Cleanup options
    /// <summary>Gets or sets how often the cleanup worker runs. Set to <see langword="null"/> to disable automatic cleanup.</summary>
    public TimeSpan? CleanupRunInterval { get; set; } = TimeSpan.FromHours(1);
    /// <summary>Gets or sets how long completed records are retained before deletion.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    /// <summary>Gets or sets how long failed records are retained before deletion.</summary>
    public TimeSpan FailedRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Gets or sets the maximum number of records deleted per cleanup iteration.</summary>
    public int CleanupBatchSize { get; set; } = 1000;

    ///// <summary>
    ///// Gets the tenant identifier provider.
    ///// TODO: utiliser un contexte de tenant plutôt que d'avoir une fonction de résolution dans les options, cela permettra d'avoir une meilleure séparation des préoccupations et de rendre le code plus modulaire et testable.En utilisant un contexte de tenant, on peut encapsuler les informations spécifiques au tenant dans une classe dédiée, ce qui facilite la gestion des données liées au tenant et améliore la maintenabilité du code.
    ///// </summary>
    ///// <value>
    ///// The tenant identifier provider.
    ///// </value>


}
