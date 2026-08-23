namespace MicroKit.Messaging.Options;

/// <summary>
/// Configuration for the inbox worker, coordinator, processor and retention worker.
/// </summary>
/// <remarks>
/// Accessors are <c>{ get; set; }</c>, not <c>{ get; init; }</c>. With <c>init</c>, the
/// <c>Action&lt;InboxProcessorOptions&gt;</c> callback taken by <c>AddMicroKitMessaging</c>
/// could not assign anything, so every inbox configuration callback was silently a no-op —
/// the same defect the outbox options carried and fixed.
/// </remarks>
public sealed record InboxProcessorOptions
{
    /// <summary>Gets or sets how many rows one pass claims. Default: 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the base polling interval, used when a pass finds work but is keeping up
    /// with it. Default: 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the ceiling the interval grows toward while the queue is idle.
    /// </summary>
    /// <remarks>
    /// Without a ceiling above <see cref="PollingInterval"/> the worker pays a round trip every
    /// few seconds forever to discover an empty table. Raising it trades latency on the first
    /// message after a quiet period for database connections. Default: 1 minute.
    /// </remarks>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the ceiling the interval grows toward while a batch-wide dependency is
    /// unreachable. Higher than <see cref="MaxPollingInterval"/>: there is nothing to gain from
    /// probing something already down. Default: 5 minutes.
    /// </summary>
    public TimeSpan DependencyUnavailableBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how long a claimed row stays leased before another processor may take it
    /// over.
    /// </summary>
    /// <remarks>
    /// The most consequential inbox setting. It must comfortably exceed the worst-case handler
    /// duration: a lease expiring while a handler is still running lets a second processor claim
    /// the row. <see cref="InboxBatchResult.LeasesLost"/> is the signal that this value is too
    /// low — without that counter the system stays correct but quietly does less work than it
    /// appears to. Default: 5 minutes.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the number of attempts after which a transiently failing row is
    /// dead-lettered. A row is dead-lettered when its incremented retry count reaches this
    /// value, so <c>MaxRetries = 5</c> means one initial attempt plus four retries. Default: 5.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the ceiling applied to the exponential back-off. Default: 1 hour.
    /// </summary>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the timeout granted to the deferred settlement write.
    /// </summary>
    /// <remarks>
    /// That write runs on its own token, independent of the shutdown token: cancelling it would
    /// strand every lease in the batch until expiry. Default: 10 seconds.
    /// </remarks>
    public TimeSpan OutcomeFlushTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum length of persisted error text. An exception message is
    /// otherwise unbounded and written verbatim on every failed attempt. Default: 2000.
    /// </summary>
    public int MaxErrorMessageLength { get; set; } = 2000;

    /// <summary>
    /// Gets or sets how many days a processed inbox row is kept before the retention worker
    /// deletes it. Zero or less disables retention entirely. Default: 30.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This window is deliberately NOT symmetric with the outbox's, and harmonising the two
    /// numbers would be a defect.</b> On the outbox, deleting early loses history. On the inbox,
    /// deleting early loses the deduplication guarantee: the table only deduplicates messages it
    /// still holds, so a row removed before its message can still be redelivered lets that
    /// message be processed a second time.
    /// </para>
    /// <para>
    /// <b>Sizing it yourself:</b> the window must exceed the maximum plausible redelivery delay
    /// of every upstream transport feeding this inbox. The default of 30 days is chosen to cover
    /// a broker dead-letter queue replayed after a long incident — it is a default, not a
    /// recommendation for every deployment. A consumer whose transport can replay later than
    /// that must raise it.
    /// </para>
    /// </remarks>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets how often the retention worker runs. Default: 1 hour.
    /// </summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}
