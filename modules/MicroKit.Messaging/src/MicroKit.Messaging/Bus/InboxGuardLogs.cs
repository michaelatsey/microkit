namespace MicroKit.Messaging.Bus;

/// <summary>Source-generated log messages of the inbox guard (#188 D6).</summary>
internal static partial class InboxGuardLogs
{
    [LoggerMessage(
        EventId = 2200,
        Level = LogLevel.Debug,
        Message = "Handler '{HandlerType}' skipped message {MessageId}: its inbox row in store " +
                  "'{Store}' is already {Status}.")]
    public static partial void Skipped(
        ILogger logger, string handlerType, Guid messageId, string store, InboxMessageStatus status);

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Debug,
        Message = "Handler '{HandlerType}' lost the race for message {MessageId}: a concurrent " +
                  "delivery committed its inbox row in store '{Store}' first. This attempt rolled back.")]
    public static partial void ConcurrentDuplicate(
        ILogger logger, string handlerType, Guid messageId, string store);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Warning,
        Message = "Handler '{HandlerType}' failed on message {MessageId}. Its work rolled back and " +
                  "its inbox row in store '{Store}' is Failed after attempt {Attempts}; the next " +
                  "attempt runs at {NextAttemptAtUtc}.")]
    public static partial void FailedRowWritten(
        ILogger logger, Exception exception, string handlerType, Guid messageId, string store,
        int attempts, DateTimeOffset nextAttemptAtUtc);

    [LoggerMessage(
        EventId = 2203,
        Level = LogLevel.Error,
        Message = "Handler '{HandlerType}' failed on message {MessageId} and is dead-lettered in " +
                  "store '{Store}' after {Attempts} counted attempt(s). Its work rolled back.")]
    public static partial void DeadLettered(
        ILogger logger, Exception exception, string handlerType, Guid messageId, string store, int attempts);

    [LoggerMessage(
        EventId = 2204,
        Level = LogLevel.Error,
        Message = "Handler '{HandlerType}' failed on message {MessageId}, and its Failed inbox row " +
                  "could not be written in store '{Store}'. The delivery is not acknowledged and " +
                  "will be redelivered.")]
    public static partial void FailedRowUnwritable(
        ILogger logger, Exception exception, string handlerType, Guid messageId, string store);

    [LoggerMessage(
        EventId = 2205,
        Level = LogLevel.Warning,
        Message = "The retry of handler '{HandlerType}' on message {MessageId} lost its claim in " +
                  "store '{Store}': another loop owns the row. Nothing was committed and no attempt " +
                  "is counted.")]
    public static partial void LeaseLost(
        ILogger logger, Exception? exception, string handlerType, Guid messageId, string store);

    [LoggerMessage(
        EventId = 2206,
        Level = LogLevel.Warning,
        Message = "Handler '{HandlerType}' committed message {MessageId} in store '{Store}', then " +
                  "an exception escaped the attempt. The work and the row are durable; it is not " +
                  "retried.")]
    public static partial void FaultAfterCommit(
        ILogger logger, Exception exception, string handlerType, Guid messageId, string store);

    [LoggerMessage(
        EventId = 2208,
        Level = LogLevel.Debug,
        Message = "Handler '{HandlerType}' failed on message {MessageId}, but another delivery " +
                  "already owns its inbox row in store '{Store}'. Nothing is written; acknowledged.")]
    public static partial void FailureAlreadyOwned(
        ILogger logger, Exception exception, string handlerType, Guid messageId, string store);

    [LoggerMessage(
        EventId = 2207,
        Level = LogLevel.Error,
        Message = "Handler '{HandlerType}' found no inbox store for message {MessageId}: store " +
                  "'{Store}' registers no IInboxGuardStore under its key. The delivery is not " +
                  "acknowledged and will be redelivered.")]
    public static partial void GuardUnregistered(
        ILogger logger, string handlerType, Guid messageId, string store);

    [LoggerMessage(
        EventId = 2209,
        Level = LogLevel.Error,
        Message = "Handler '{HandlerType}' could not reach its inbox row for message {MessageId}: store " +
                  "'{Store}' reports itself unreachable. A dependency outage: nothing is charged, and " +
                  "the delivery waits — a transport pauses and redelivers, a local row is deferred.")]
    public static partial void GuardUnreachable(
        ILogger logger, Exception exception, string handlerType, Guid messageId, string store);
}
