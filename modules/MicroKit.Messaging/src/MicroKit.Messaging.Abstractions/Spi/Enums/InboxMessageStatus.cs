namespace MicroKit.Messaging.Spi;

/// <summary>
/// Represents the lifecycle state of a message in the transactional inbox.
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>): persisted state a store reads and writes, not
/// an application concern.
/// </para>
/// <para>
/// <b>One row per (message, handler), written once (#188).</b> A delivery that succeeds writes its
/// row <see cref="Processed"/>, in the transaction that commits the handler's work. A delivery
/// that fails writes it <see cref="Failed"/>, in a transaction of its own, after the handler's
/// transaction rolled back. From then on the store's retry loop owns it.
/// </para>
/// <para>
/// State machine (retry): <c>Failed → Processing</c> through the atomic claim, then
/// <c>Processing → Processed</c> in the handler's transaction, or <c>Processing → Failed</c>
/// with the attempt counted and the next attempt scheduled, or released back to
/// <see cref="Failed"/> with nothing counted when the attempt never ran.
/// </para>
/// <para>
/// <b>Terminal</b> means <see cref="Processed"/>, or <see cref="Failed"/> with
/// <c>InboxMessage.DeadLettered = true</c>: permanently unprocessable, or out of attempts.
/// </para>
/// </remarks>
public enum InboxMessageStatus
{
    /// <summary>
    /// Recorded and not yet handled. Written by no path since #188, and still claimed by the retry
    /// loop, so a row written by an earlier version is handled.
    /// </summary>
    Received,

    /// <summary>
    /// Claimed by a store's retry loop: the lease is held until <c>LockedUntilUtc</c>, and
    /// <c>ClaimToken</c> names the loop that owns the row.
    /// </summary>
    Processing,

    /// <summary>
    /// Handled: written in the transaction that committed the handler's work. Terminal.
    /// </summary>
    Processed,

    /// <summary>
    /// The last attempt failed. With <c>DeadLettered = false</c> the row is retried once
    /// <c>NextRetryAtUtc</c> has passed; with <c>DeadLettered = true</c> it is terminal.
    /// </summary>
    Failed,
}
