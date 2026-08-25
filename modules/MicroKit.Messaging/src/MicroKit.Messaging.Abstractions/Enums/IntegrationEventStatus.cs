namespace MicroKit.Messaging;

/// <summary>
/// Represents the lifecycle state of a staged integration event awaiting delivery.
/// </summary>
/// <remarks>
/// <para>
/// Answers "where is this message now" and changes on every transition. Whether delivery was ever
/// permanently abandoned is a separate, monotone question answered by
/// <see cref="IntegrationEventMessage.DeadLettered"/> — see that property for why the two are not
/// folded into a single <c>Failed</c> value.
/// </para>
/// <para>
/// The relay that drives these transitions is not implemented yet. Only
/// <see cref="Pending"/> is ever written today, by
/// <see cref="IIntegrationEventPublisher.PublishAsync{TEvent}"/>.
/// </para>
/// </remarks>
public enum IntegrationEventStatus
{
    /// <summary>
    /// Staged, not yet delivered. Also the state a message returns to after a failed attempt or a
    /// release. Claimable when it is not dead-lettered and <c>NextRetryAtUtc</c> is
    /// <see langword="null"/> or has elapsed.
    /// </summary>
    Pending,

    /// <summary>
    /// Claimed by a relay; delivery in progress until the lease expires
    /// (<c>LockedUntilUtc</c>) or the attempt settles.
    /// </summary>
    Processing,

    /// <summary>Delivery confirmed. Terminal.</summary>
    Published,
}
