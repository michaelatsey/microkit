using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Messaging.Core.Configuration;

/// <summary>
/// Réprésente les options de configuration pour la boîte de réception (Inbox) du système de messagerie, permettant de définir les paramètres liés à la réception et au traitement des messages entrants.
/// </summary>
public class InboxOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="InboxOptions"/> is enabled.
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
    /// Gets or sets the maximum processing attempts.
    /// </summary>
    /// <value>
    /// The maximum processing attempts.
    /// </value>
    public int MaxProcessingAttempts { get; set; } = 3;
    /// <summary>
    /// Gets or sets the maximum degree of parallelism.
    /// </summary>
    /// <value>
    /// The maximum degree of parallelism.
    /// </value>
    public int  MaxDegreeOfParallelism { get; set; } = 2;
    /// <summary>
    /// Gets or sets the message retention.
    /// </summary>
    /// <value>
    /// The message retention.
    /// </value>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the failed retention period.
    /// </summary>
    /// <value>
    /// The failed retention period.
    /// </value>
    public TimeSpan FailedRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
    /// <summary>
    /// Gets or sets a value indicating whether [automatic cleanup].
    /// </summary>
    /// <value>
    ///   <c>true</c> if [automatic cleanup]; otherwise, <c>false</c>.
    /// </value>
    public bool AutomaticCleanup { get; set; } = true;
    /// <summary>
    /// Gets or sets the cleanup interval.
    /// </summary>
    /// <value>
    /// The cleanup interval.
    /// </value>
    public TimeSpan? CleanupRunInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Nombre de messages supprimés à chaque itération du nettoyage.
    /// Évite de verrouiller la table trop longtemps.
    /// </summary>
    /// <summary>Gets or sets the maximum number of records deleted per cleanup iteration.</summary>
    public int CleanupBatchSize { get; set; } = 1000;

    /// <summary>Gets or sets explicit consumer type names to register. When empty, assemblies are scanned automatically.</summary>
    public List<string> CustomConsumers { get; set; } = [];
}
