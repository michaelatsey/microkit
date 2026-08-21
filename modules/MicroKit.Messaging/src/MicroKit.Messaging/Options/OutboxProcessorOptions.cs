namespace MicroKit.Messaging.Options;

/// <summary>
/// Configuration options for the outbox worker, coordinator, processor and retention worker.
/// </summary>
/// <remarks>
/// <para>
/// Properties keep their original names and meanings, so existing configuration binds unchanged.
/// Two <b>behavioural</b> defaults did change: <see cref="BatchSize"/> 20 to 100 and
/// <see cref="MaxRetries"/> 10 to 5. Both bind unchanged but halve the retry budget.
/// </para>
/// <para>
/// The accessors are <c>{ get; set; }</c> rather than <c>init</c>. With <c>init</c>, the
/// <c>Action&lt;OutboxProcessorOptions&gt;</c> callback taken by <c>AddMicroKitMessaging</c> could
/// not assign anything, so every outbox configuration callback was silently a no-op.
/// </para>
/// </remarks>
public sealed record OutboxProcessorOptions
{
    /// <summary>
    /// Gets or sets the maximum number of outbox messages claimed per pass. Default: 100.
    /// </summary>
    /// <remarks>
    /// Raised from 20. With a set-based claim and a batched settlement, the per-batch cost no
    /// longer scales with the batch size, so a larger batch is strictly cheaper per message.
    /// </remarks>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the base polling interval, used when a pass finds work but is keeping up
    /// with it. Default: 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the ceiling the polling interval grows toward while the queue is idle.
    /// Default: 1 minute.
    /// </summary>
    /// <remarks>
    /// Without a ceiling above <see cref="PollingInterval"/> the worker pays a round trip every
    /// few seconds forever to discover an empty table. Raising it trades latency on the first
    /// message after a quiet period for database connections.
    /// </remarks>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the ceiling the polling interval grows toward while the transport is
    /// unreachable. Default: 5 minutes.
    /// </summary>
    /// <remarks>
    /// Higher than <see cref="MaxPollingInterval"/>: there is nothing to gain from probing a
    /// broker that is already down.
    /// </remarks>
    public TimeSpan TransportUnavailableBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how long a claimed message stays leased before another processor may take
    /// it over. Default: 5 minutes.
    /// </summary>
    /// <remarks>
    /// Must comfortably exceed the worst-case dispatch duration for a whole batch: too short and
    /// a slow batch is duplicated, too long and a crashed processor stalls its messages for that
    /// duration.
    /// </remarks>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the number of dispatch attempts after which a transiently failing message is
    /// dead-lettered. Default: 5.
    /// </summary>
    /// <remarks>
    /// A message is dead-lettered when its incremented retry count reaches this value, so
    /// <c>MaxRetries = 5</c> means one initial attempt plus four retries. Lowered from 10: a
    /// permanently undeliverable payload now dead-letters on first sight via
    /// <see cref="OutboxPayloadException"/>, and a transport outage no longer consumes retries at
    /// all, so the budget no longer has to absorb either.
    /// </remarks>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the ceiling applied to the exponential back-off before jitter.
    /// Default: 1 hour.
    /// </summary>
    /// <remarks>
    /// Full back-off formula:
    /// <c>NextRetryAtUtc = now + Uniform(0, min(2^RetryCount seconds, MaxRetryBackoff))</c>.
    /// </remarks>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the timeout granted to the settlement write. Default: 10 seconds.
    /// </summary>
    /// <remarks>
    /// That write runs on its own token, independent of the shutdown token. Cancelling it would
    /// strand every lease in the batch and cause already-dispatched messages to be dispatched
    /// again, so a shutdown must be allowed a brief window to settle.
    /// </remarks>
    public TimeSpan OutcomeFlushTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum length of persisted error text. Default: 2000.
    /// </summary>
    /// <remarks>
    /// An exception message is otherwise unbounded and written verbatim on every failed attempt.
    /// The default fits the 2048-character <c>ErrorMessage</c> column.
    /// </remarks>
    public int MaxErrorMessageLength { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the number of days to retain <c>Published</c> outbox rows before the
    /// retention worker deletes them. Default: 7.
    /// </summary>
    /// <remarks>
    /// A value of zero or less disables retention entirely: the worker logs once and stops.
    /// </remarks>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets how often the retention worker runs one cleanup pass. Default: 1 hour.
    /// </summary>
    /// <remarks>
    /// Retention is housekeeping, not a hot path — a slow timer is deliberate.
    /// </remarks>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);
}
