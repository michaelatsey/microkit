namespace MicroKit.Messaging.Local;

/// <summary>Logs of the local endpoint (#188 D7).</summary>
internal static partial class LocalLogs
{
    [LoggerMessage(
        EventId = 2300,
        Level = LogLevel.Debug,
        Message = "Local message {MessageId} ('{MessageType}') enlisted in store '{Store}'.")]
    public static partial void Enlisted(ILogger logger, Guid messageId, string messageType, string store);

    [LoggerMessage(
        EventId = 2301,
        Level = LogLevel.Debug,
        Message = "Local message {MessageId} ('{MessageType}') queued in memory: no store is configured.")]
    public static partial void Queued(ILogger logger, Guid messageId, string messageType);

    [LoggerMessage(
        EventId = 2302,
        Level = LogLevel.Error,
        Message = "Local message {MessageId} ('{Type}') was dead-lettered for consumer '{ConsumerType}' " +
                  "after {FailedAttempts} failed attempt(s): {Reason}")]
    public static partial void DeadLettered(
        ILogger logger, Exception? exception, Guid messageId, string type, string consumerType, int failedAttempts, string? reason);

    [LoggerMessage(
        EventId = 2303,
        Level = LogLevel.Warning,
        Message = "Local message {MessageId} ('{Type}') failed attempt {FailedAttempts} of " +
                  "{MaxDeliveryAttempts} for consumer '{ConsumerType}' and is redelivered to it.")]
    public static partial void Redelivered(
        ILogger logger, Guid messageId, string type, string consumerType, int failedAttempts, int maxDeliveryAttempts);

    [LoggerMessage(
        EventId = 2304,
        Level = LogLevel.Warning,
        Message = "A dependency is unavailable while consumer '{ConsumerType}' handles local message " +
                  "{MessageId} ('{Type}'). The local queue pauses for {Backoff}; the message is redelivered " +
                  "to it without counting an attempt.")]
    public static partial void Paused(
        ILogger logger, Guid messageId, string type, string consumerType, TimeSpan backoff);

    [LoggerMessage(
        EventId = 2305,
        Level = LogLevel.Error,
        Message = "The local queue's loop failed outside any message; it runs again in {RetryIn}.")]
    public static partial void LoopFaulted(ILogger logger, Exception exception, TimeSpan retryIn);
}
