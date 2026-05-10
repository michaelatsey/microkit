using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Configuration;

/// <summary>
/// Réprésente les options de configuration pour la boîte d'envoi (Outbox) du système de messagerie, permettant de définir les paramètres liés à l'envoi et à la gestion des messages à envoyer.
/// </summary>
public class OutboxOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="OutboxOptions"/> is enabled.
    /// </summary>
    /// <value>
    ///   <c>true</c> if enabled; otherwise, <c>false</c>.
    /// </value>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the size of the batch.
    /// </summary>
    /// <value>
    /// The size of the batch.
    /// </value>
    public int BatchSize { get; set; } = 100;
    /// <summary>
    /// Gets or sets the polling interval.
    /// </summary>
    /// <value>
    /// The polling interval.
    /// </value>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Gets or sets the lock duration in minutes.
    /// </summary>
    /// <value>
    /// The lock duration in minutes.
    /// </value>
    public TimeSpan LockDurationInMinutes { get; set; } = TimeSpan.FromMicroseconds(15);
    /// <summary>
    /// Gets or sets the maximum retry count.
    /// </summary>
    /// <value>
    /// The maximum retry count.
    /// </value>
    public int MaxRetryCount { get; set; } = 5;
    /// <summary>
    /// Gets or sets the retry delay.
    /// </summary>
    /// <value>
    /// The retry delay.
    /// </value>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>
    /// Gets or sets a value indicating whether [use exponential backoff].
    /// </summary>
    /// <value>
    ///   <c>true</c> if [use exponential backoff]; otherwise, <c>false</c>.
    /// </value>
    public bool UseExponentialBackoff { get; set; } = true;
    /// <summary>
    /// Le "Time To Live" (TTL) des messages en 'pending' dans la boîte d'envoi avant qu'ils ne soient considérés comme expirés 
    /// et éligibles pour le nettoyage, par défaut 7 jours.
    /// </summary>
    /// <value>
    /// The message expiration.
    /// </value>
    public TimeSpan MessageExpiration { get; set; } = TimeSpan.FromDays(7);

    public bool CleanupEnabled { get; set; } = true;

    /// <summary>
    /// Intervalle d'exécution du cycle de nettoyage.
    /// Par défaut, une fois par heure pour ne pas surcharger la DB.
    /// </summary>
    public TimeSpan? CleanupRunInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Durée de conservation des messages PUBLIÉS avant suppression.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the failed retention period.
    /// </summary>
    /// <value>
    /// The failed retention period.
    /// </value>
    public TimeSpan FailedRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Taille du batch spécifique pour la suppression (peut être plus grand que celui de l'envoi).
    /// </summary>
    public int CleanupBatchSize { get; set; } = 1000;


    /// <summary>
    /// Gets or sets the instance identifier.
    /// </summary>
    /// <value>
    /// The instance identifier.
    /// </value>
    public string? InstanceId { get; set; }
}
