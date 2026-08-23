namespace MicroKit.Messaging.Processing;

/// <summary>Source-generated log methods for <see cref="InboxProcessor"/>.</summary>
/// <remarks>
/// <c>[LoggerMessage]</c> emits a cached delegate per message, so a log call allocates nothing
/// and boxes no argument, and the generator inserts the <c>IsEnabled</c> guard. Event ids
/// 2000–2023 are the inbox range; the outbox owns 1000–1023.
/// </remarks>
internal static partial class InboxProcessorLogs
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug,
        Message = "Inbox batch claimed {ClaimedCount} row(s).")]
    public static partial void BatchClaimed(ILogger logger, int claimedCount);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Inbox message {MessageId} (consumer: {ConsumerType}) failed transiently " +
                  "(attempt {Attempt}/{MaxRetries}). Next attempt at {NextRetryAtUtc:O}.")]
    public static partial void TransientFailure(
        ILogger logger, Exception exception, Guid messageId, string consumerType,
        int attempt, int maxRetries, DateTimeOffset nextRetryAtUtc);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "Inbox message {MessageId} (consumer: {ConsumerType}) exhausted its retry " +
                  "budget ({MaxRetries}). Dead-lettering.")]
    public static partial void RetriesExhausted(
        ILogger logger, Exception exception, Guid messageId, string consumerType, int maxRetries);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error,
        Message = "Inbox message {MessageId} (consumer: {ConsumerType}) is permanently " +
                  "unprocessable. Dead-lettering without retry.")]
    public static partial void PermanentFailure(
        ILogger logger, Exception exception, Guid messageId, string consumerType);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error,
        Message = "Inbox dependency unavailable. Abandoning batch and releasing " +
                  "{ReleasedCount} unattempted row(s) — no retry budget consumed.")]
    public static partial void DependencyUnavailable(
        ILogger logger, Exception exception, int releasedCount);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information,
        Message = "Inbox batch cancelled. Releasing {ReleasedCount} unattempted row(s).")]
    public static partial void BatchCancelled(ILogger logger, int releasedCount);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Critical,
        Message = "A required inbox service cannot be resolved — the inbox is misconfigured. " +
                  "Abandoning batch and releasing {ReleasedCount} row(s) with no retry " +
                  "consumed. Fix the registration; retrying will not help.")]
    public static partial void ServiceUnresolvable(
        ILogger logger, Exception exception, int releasedCount);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning,
        Message = "Inbox lease lost for message {MessageId} (consumer: {ConsumerType}) before " +
                  "the handler ran. Another processor owns it; skipping to avoid duplicating " +
                  "side effects. Recurring occurrences mean LeaseDuration is too short.")]
    public static partial void LeaseLostBeforeHandler(
        ILogger logger, Guid messageId, string consumerType);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Warning,
        Message = "Inbox lease for message {MessageId} (consumer: {ConsumerType}) expired while " +
                  "its handler was running. The handler transaction was rolled back and another " +
                  "processor now owns the row. Increase LeaseDuration above the worst-case " +
                  "handler duration.")]
    public static partial void LeaseLostDuringHandler(
        ILogger logger, Exception exception, Guid messageId, string consumerType);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Warning,
        Message = "Handler for message {MessageId} (consumer: {ConsumerType}) succeeded but " +
                  "committed no unit of work, so the processed mark was never persisted with it. " +
                  "Falling back to a deferred write: processing for this row was at-least-once, " +
                  "not transactionally atomic. Make the handler write through the execution " +
                  "scope's DbContext to restore the guarantee.")]
    public static partial void HandlerDidNotCommit(
        ILogger logger, Guid messageId, string consumerType);

    /// <remarks>
    /// The message deliberately names both causes instead of asserting one. Zero rows means the
    /// claim token no longer matches, and the token is cleared by two different events: a lease
    /// that expired and was re-claimed, or a success that already committed. Asserting "the lease
    /// expired" would cry wolf on a nominal path. Cross-check against event 2008 (lease lost
    /// during handler) and 2012 (post-commit fault) before treating this as an incident.
    /// </remarks>
    [LoggerMessage(EventId = 2010, Level = LogLevel.Warning,
        Message = "Inbox settlement wrote {WrittenCount} of {OutcomeCount} outcome(s). The " +
                  "unwritten rows no longer carry this batch's claim token — their lease either " +
                  "expired and was re-claimed, or their handler already committed.")]
    public static partial void PartialSettlement(ILogger logger, int writtenCount, int outcomeCount);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Error,
        Message = "Failed to settle inbox batch of {OutcomeCount} row(s). Leases will expire " +
                  "naturally and the rows will be re-claimed.")]
    public static partial void SettlementFailed(ILogger logger, Exception exception, int outcomeCount);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Warning,
        Message = "Handler for message {MessageId} (consumer: {ConsumerType}) committed its unit " +
                  "of work and then threw. The row is durably Processed, so this is a post-commit " +
                  "fault, not a processing failure — it is reported, not retried.")]
    public static partial void PostCommitFault(
        ILogger logger, Exception exception, Guid messageId, string consumerType);

    [LoggerMessage(EventId = 2020, Level = LogLevel.Debug,
        Message = "Inbox retention pass deleted {DeletedCount} processed row(s) older than " +
                  "{Cutoff:O}.")]
    public static partial void RetentionPass(ILogger logger, int deletedCount, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Information,
        Message = "Inbox retention is disabled (RetentionDays = {RetentionDays}). Processed rows " +
                  "are kept indefinitely, which preserves deduplication but grows the table " +
                  "without bound.")]
    public static partial void RetentionDisabled(ILogger logger, int retentionDays);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Error,
        Message = "Inbox retention pass failed. Retrying after {Interval}.")]
    public static partial void RetentionFailed(ILogger logger, Exception exception, TimeSpan interval);

    [LoggerMessage(EventId = 2023, Level = LogLevel.Critical,
        Message = "IInboxRetentionStore is not registered — stopping the inbox retention worker. " +
                  "Register it (e.g. call AddEfCoreOutbox()).")]
    public static partial void RetentionStoreUnresolvable(ILogger logger, Exception exception);
}
