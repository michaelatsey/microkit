namespace MicroKit.Messaging.Processing;

/// <summary>
/// Source-generated log methods for the inbox ingestion path — <see cref="EnvelopeReceiver"/> and
/// any broker adapter that records rows itself.
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

    /// <remarks>
    /// <b>Warning, and the level is the whole point.</b> Writing no row is the correct behaviour
    /// for a contract nothing here consumes, and it is also exactly what a broken composition
    /// produces — <c>Consumes&lt;T&gt;()</c> declared, <c>AddMessageHandler&lt;,&gt;()</c>
    /// forgotten. The two are indistinguishable from the outside: no row is written, the drain
    /// claims nothing, and the queue looks healthy and idle. Silent success is forbidden in this
    /// module, so the honest signal is emitted at the only moment anything knows the difference
    /// might matter.
    /// <para>
    /// The local type is named alongside the wire name because the wire name alone does not say
    /// which registration is missing — the binding resolved, so the fault is on the handler side.
    /// </para>
    /// </remarks>
    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning,
        Message = "Contract '{ContractName}' from '{Source}' resolves to '{EventType}', but no " +
                  "message handler is registered for it. No inbox row was written and the " +
                  "message will not be delivered anywhere in this process. Register a handler " +
                  "with AddMessageHandler<THandler, TEvent>(), or stop subscribing to the " +
                  "contract.")]
    public static partial void NoConsumer(
        ILogger logger, string contractName, string source, string eventType);
}
