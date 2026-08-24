namespace MicroKit.Messaging;

/// <summary>
/// The disposition of one inbox row, buffered in memory during batch processing and applied to
/// the store by one <c>ApplyOutcomesAsync</c> call once the batch ends.
/// </summary>
/// <remarks>
/// <para>
/// A <c>readonly record struct</c> so that buffering a batch costs one array, not one heap
/// allocation per row.
/// </para>
/// <para>
/// Only failures and releases are buffered. A row handled successfully settles itself inside
/// the handler's own transaction through <see cref="IInboxSettlementStore"/> and produces no
/// outcome at all — the one exception being
/// <see cref="InboxOutcomeKind.Processed"/>, written when the handler committed no unit of work
/// and the staged mark therefore never reached the database.
/// </para>
/// </remarks>
/// <param name="Key">The row this outcome applies to.</param>
/// <param name="Kind">What the store must persist for this row.</param>
/// <param name="RetryCount">
/// The retry count to persist. Meaningful for <see cref="InboxOutcomeKind.Retry"/> and
/// <see cref="InboxOutcomeKind.DeadLetter"/>; zero otherwise.
/// </param>
/// <param name="NextRetryAtUtc">
/// Earliest re-eligibility. Non-null only for <see cref="InboxOutcomeKind.Retry"/>.
/// </param>
/// <param name="ErrorMessage">Truncated failure text. Non-null only for failure outcomes.</param>
public readonly record struct InboxOutcome(
    InboxMessageKey Key,
    InboxOutcomeKind Kind,
    int RetryCount,
    DateTimeOffset? NextRetryAtUtc,
    string? ErrorMessage)
{
    /// <summary>Creates a <see cref="InboxOutcomeKind.Processed"/> outcome.</summary>
    /// <param name="key">The row whose handler succeeded without committing the staged mark.</param>
    /// <returns>The outcome to buffer.</returns>
    public static InboxOutcome Processed(InboxMessageKey key) =>
        new(key, InboxOutcomeKind.Processed, RetryCount: 0, NextRetryAtUtc: null, ErrorMessage: null);

    /// <summary>Creates a <see cref="InboxOutcomeKind.Retry"/> outcome.</summary>
    /// <param name="key">The row that failed transiently.</param>
    /// <param name="retryCount">The incremented retry count to persist.</param>
    /// <param name="nextRetryAtUtc">Earliest eligibility for the next attempt.</param>
    /// <param name="errorMessage">Truncated failure text.</param>
    /// <returns>The outcome to buffer.</returns>
    public static InboxOutcome Retry(
        InboxMessageKey key,
        int retryCount,
        DateTimeOffset nextRetryAtUtc,
        string? errorMessage) =>
        new(key, InboxOutcomeKind.Retry, retryCount, nextRetryAtUtc, errorMessage);

    /// <summary>Creates a <see cref="InboxOutcomeKind.DeadLetter"/> outcome.</summary>
    /// <param name="key">The row that is permanently unprocessable.</param>
    /// <param name="retryCount">The retry count to persist.</param>
    /// <param name="errorMessage">Truncated failure text.</param>
    /// <returns>The outcome to buffer.</returns>
    public static InboxOutcome DeadLetter(
        InboxMessageKey key,
        int retryCount,
        string? errorMessage) =>
        new(key, InboxOutcomeKind.DeadLetter, retryCount, NextRetryAtUtc: null, errorMessage);

    /// <summary>Creates a <see cref="InboxOutcomeKind.Released"/> outcome.</summary>
    /// <param name="key">The row that was claimed but never attempted.</param>
    /// <returns>The outcome to buffer.</returns>
    public static InboxOutcome Released(InboxMessageKey key) =>
        new(key, InboxOutcomeKind.Released, RetryCount: 0, NextRetryAtUtc: null, ErrorMessage: null);
}
