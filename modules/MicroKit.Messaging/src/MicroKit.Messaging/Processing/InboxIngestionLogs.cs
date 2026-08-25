namespace MicroKit.Messaging.Processing;

/// <summary>
/// Source-generated log methods for the inbox ingestion path, for broker adapters and the
/// receiving seam that turns a <see cref="MessageEnvelope"/> into inbox rows.
/// <para>
/// ⚠ <b>No caller ships in this release.</b> The in-process fan-out that used these was withdrawn
/// with the in-process transport (ADR-MSG-019). They are kept rather than deleted and re-added
/// because ingestion needs exactly this pair — one row written, one redelivery deduplicated — and
/// the wording of a log message is a thing operators build alerts on.
/// </para>
/// </summary>
/// <remarks>
/// Separate from <see cref="InboxProcessorLogs"/> because ingestion and drain are separate
/// concerns with separate operators: a rising deduplication rate is an ingestion-side signal,
/// while a rising lease-loss rate is a drain-side one.
/// </remarks>
internal static partial class InboxIngestionLogs
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Debug,
        Message = "Inbox recorded message {MessageId} for consumer {ConsumerType}.")]
    public static partial void Added(ILogger logger, Guid messageId, string consumerType);

    /// <remarks>
    /// <b>Debug, deliberately not Warning.</b> Under at-least-once delivery a redelivery is
    /// normal operation, not an anomaly: one expired lease after a crash produces a burst of
    /// these. Logging them as warnings would drown the log after any incident and train whoever
    /// reads it to lower the level — losing the genuine warnings with them. The rate is what
    /// matters, and a counter carries a rate better than a log line.
    /// </remarks>
    [LoggerMessage(EventId = 2101, Level = LogLevel.Debug,
        Message = "Inbox already holds message {MessageId} for consumer {ConsumerType}. " +
                  "Redelivery deduplicated — not an error.")]
    public static partial void Deduplicated(ILogger logger, Guid messageId, string consumerType);
}
