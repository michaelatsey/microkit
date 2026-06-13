namespace MicroKit.Messaging;

/// <summary>
/// Represents the lifecycle state of a message in the transactional inbox.
/// </summary>
/// <remarks>
/// State machine: <c>Received → Processing → Processed</c> (happy path);
/// <c>Received → Processing → Failed</c> (handler error — retried until max retries exceeded).
/// </remarks>
public enum InboxMessageStatus
{
    /// <summary>
    /// Message received and recorded; handler not yet invoked.
    /// </summary>
    Received,

    /// <summary>
    /// Handler executing; lease held until <c>LockedUntilUtc</c>.
    /// </summary>
    Processing,

    /// <summary>
    /// Handler completed successfully. Terminal.
    /// </summary>
    Processed,

    /// <summary>
    /// Handler invocation failed. Unlike <see cref="OutboxMessageStatus.Failed"/>,
    /// this state is used for both transient failures (retry pending) and terminal
    /// failures (max retries exceeded), distinguished by <c>RetryCount</c>.
    /// </summary>
    Failed,
}
