namespace MicroKit.Messaging;

/// <summary>
/// The disposition of one message, buffered in memory during batch processing and applied
/// to the store by one <c>ApplyOutcomesAsync</c> call once the batch ends.
/// </summary>
/// <remarks>
/// A <c>readonly record struct</c> so that buffering an entire batch costs one array,
/// not one heap allocation per message.
/// </remarks>
/// <param name="MessageId">Identifier of the message this outcome applies to.</param>
/// <param name="Kind">What the store must persist for this message.</param>
/// <param name="RetryCount">
/// The retry count to persist. Meaningful for <see cref="OutboxOutcomeKind.Retry"/>
/// and <see cref="OutboxOutcomeKind.DeadLetter"/>; zero otherwise.
/// </param>
/// <param name="NextRetryAtUtc">
/// Earliest eligibility for re-dispatch. Non-null only for <see cref="OutboxOutcomeKind.Retry"/>.
/// </param>
/// <param name="ErrorMessage">
/// Truncated failure text. Non-null only for failure outcomes.
/// </param>
public readonly record struct OutboxOutcome(
    MessageId MessageId,
    OutboxOutcomeKind Kind,
    int RetryCount,
    DateTimeOffset? NextRetryAtUtc,
    string? ErrorMessage)
{
    /// <summary>Creates a <see cref="OutboxOutcomeKind.Published"/> outcome.</summary>
    /// <param name="messageId">The message that was dispatched successfully.</param>
    /// <returns>The outcome to buffer.</returns>
    public static OutboxOutcome Published(MessageId messageId) =>
        new(messageId, OutboxOutcomeKind.Published, RetryCount: 0, NextRetryAtUtc: null, ErrorMessage: null);

    /// <summary>Creates a <see cref="OutboxOutcomeKind.Retry"/> outcome.</summary>
    /// <param name="messageId">The message that failed transiently.</param>
    /// <param name="retryCount">The incremented retry count to persist.</param>
    /// <param name="nextRetryAtUtc">Earliest eligibility for the next attempt.</param>
    /// <param name="errorMessage">Truncated failure text.</param>
    /// <returns>The outcome to buffer.</returns>
    public static OutboxOutcome Retry(
        MessageId messageId,
        int retryCount,
        DateTimeOffset nextRetryAtUtc,
        string? errorMessage) =>
        new(messageId, OutboxOutcomeKind.Retry, retryCount, nextRetryAtUtc, errorMessage);

    /// <summary>Creates a <see cref="OutboxOutcomeKind.DeadLetter"/> outcome.</summary>
    /// <param name="messageId">The message that is permanently undeliverable.</param>
    /// <param name="retryCount">The retry count to persist.</param>
    /// <param name="errorMessage">Truncated failure text.</param>
    /// <returns>The outcome to buffer.</returns>
    public static OutboxOutcome DeadLetter(
        MessageId messageId,
        int retryCount,
        string? errorMessage) =>
        new(messageId, OutboxOutcomeKind.DeadLetter, retryCount, NextRetryAtUtc: null, errorMessage);

    /// <summary>Creates a <see cref="OutboxOutcomeKind.Released"/> outcome.</summary>
    /// <param name="messageId">The message that was claimed but never attempted.</param>
    /// <returns>The outcome to buffer.</returns>
    public static OutboxOutcome Released(MessageId messageId) =>
        new(messageId, OutboxOutcomeKind.Released, RetryCount: 0, NextRetryAtUtc: null, ErrorMessage: null);
}
