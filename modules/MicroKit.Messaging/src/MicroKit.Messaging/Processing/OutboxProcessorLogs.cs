namespace MicroKit.Messaging.Processing;

/// <summary>
/// Source-generated log methods for <see cref="OutboxProcessor"/> and
/// <see cref="OutboxRetentionWorker"/>.
/// </summary>
/// <remarks>
/// <c>[LoggerMessage]</c> emits a cached delegate per message, so a log call allocates
/// nothing and boxes no argument, and the generator inserts the <c>IsEnabled</c> guard —
/// the manual guard the previous implementation wrote by hand is no longer needed.
/// </remarks>
internal static partial class OutboxProcessorLogs
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Outbox batch claimed {ClaimedCount} message(s).")]
    public static partial void BatchClaimed(ILogger logger, int claimedCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} ({EventType}) failed transiently " +
                  "(attempt {Attempt}/{MaxRetries}). Next attempt at {NextRetryAtUtc:O}.")]
    public static partial void TransientFailure(
        ILogger logger,
        Exception exception,
        Guid messageId,
        string eventType,
        int attempt,
        int maxRetries,
        DateTimeOffset nextRetryAtUtc);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}) exhausted its retry budget " +
                  "({MaxRetries}). Dead-lettering.")]
    public static partial void RetriesExhausted(
        ILogger logger, Exception exception, Guid messageId, string eventType, int maxRetries);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}) is permanently undeliverable. " +
                  "Dead-lettering without retry.")]
    public static partial void PermanentFailure(
        ILogger logger, Exception exception, Guid messageId, string eventType);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Outbox transport unavailable. Abandoning batch and releasing " +
                  "{ReleasedCount} unattempted message(s) — no retry budget consumed.")]
    public static partial void TransportUnavailable(
        ILogger logger, Exception exception, int releasedCount);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Outbox batch cancelled. Releasing {ReleasedCount} unattempted message(s).")]
    public static partial void BatchCancelled(ILogger logger, int releasedCount);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Critical,
        Message = "IOutboxDispatcher cannot be resolved — the outbox is misconfigured. " +
                  "Abandoning batch and releasing {ReleasedCount} message(s) with no retry " +
                  "consumed. Register a dispatcher; retrying will not help.")]
    public static partial void DispatcherUnresolvable(
        ILogger logger, Exception exception, int releasedCount);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "Outbox settlement wrote {WrittenCount} of {OutcomeCount} outcome(s). " +
                  "The missing leases expired mid-batch and are now owned elsewhere.")]
    public static partial void PartialSettlement(ILogger logger, int writtenCount, int outcomeCount);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "Failed to settle outbox batch of {OutcomeCount} message(s). " +
                  "Leases will expire naturally; dispatched messages may be redelivered.")]
    public static partial void SettlementFailed(ILogger logger, Exception exception, int outcomeCount);

    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Debug,
        Message = "Outbox retention deleted {DeletedCount} published message(s) processed before {Cutoff:O}.")]
    public static partial void RetentionPass(ILogger logger, int deletedCount, DateTimeOffset cutoff);

    [LoggerMessage(
        EventId = 1021,
        Level = LogLevel.Information,
        Message = "Outbox retention is disabled (RetentionDays = {RetentionDays}). " +
                  "Published rows will accumulate until a retention policy is configured.")]
    public static partial void RetentionDisabled(ILogger logger, int retentionDays);

    [LoggerMessage(
        EventId = 1022,
        Level = LogLevel.Error,
        Message = "Outbox retention pass failed. Retrying after {Interval}.")]
    public static partial void RetentionFailed(ILogger logger, Exception exception, TimeSpan interval);

    [LoggerMessage(
        EventId = 1023,
        Level = LogLevel.Critical,
        Message = "IOutboxRetentionStore cannot be resolved — stopping the retention worker. " +
                  "Register a store (e.g. call AddEfCoreOutbox()); retrying will not help.")]
    public static partial void RetentionStoreUnresolvable(ILogger logger, Exception exception);
}
