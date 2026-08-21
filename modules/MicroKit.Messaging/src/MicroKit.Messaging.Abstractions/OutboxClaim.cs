namespace MicroKit.Messaging;

/// <summary>
/// The result of one atomic claim: the messages this processor won, and the token
/// that proves ownership of them.
/// </summary>
/// <remarks>
/// <para>
/// The token is what makes the lease verifiable on release, not only on acquisition.
/// Every terminal write filters on it, so a processor whose lease expired mid-dispatch
/// cannot overwrite the state of the processor that took its messages over. Without
/// it, a late writer silently clobbers a legitimate one — a lost update that only
/// manifests under lease expiry, which is to say under load or after a stall.
/// </para>
/// <para>
/// The token is carried explicitly rather than held as store state, so that the store
/// stays stateless and the ownership relation between a claim and its outcomes is
/// visible in the type system.
/// </para>
/// </remarks>
/// <param name="Token">Ownership proof for this batch. Written to every claimed row.</param>
/// <param name="Messages">The messages won. Empty when nothing was claimable.</param>
public sealed record OutboxClaim(Guid Token, IReadOnlyList<OutboxMessage> Messages)
{
    /// <summary>Gets an empty claim — nothing was available.</summary>
    public static OutboxClaim Empty { get; } = new(Guid.Empty, []);

    /// <summary>Gets the number of messages claimed.</summary>
    public int Count => Messages.Count;
}
