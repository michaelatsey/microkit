namespace MicroKit.Messaging;

/// <summary>
/// The result of one atomic inbox claim: the rows this processor won, and the token that
/// proves ownership of them.
/// </summary>
/// <remarks>
/// <para>
/// The token is what makes the lease verifiable on release, not only on acquisition. Every
/// terminal write filters on it, so a processor whose lease expired mid-handler cannot
/// overwrite the processor that took its rows over.
/// </para>
/// <para>
/// It is carried explicitly rather than held as store state, so the ownership relation between
/// a claim and its outcomes is visible in the type system.
/// </para>
/// </remarks>
/// <param name="Token">Ownership proof for this batch. Written to every claimed row.</param>
/// <param name="Messages">The rows won. Empty when nothing was claimable.</param>
public sealed record InboxClaim(Guid Token, IReadOnlyList<InboxMessage> Messages)
{
    /// <summary>Gets an empty claim — nothing was available.</summary>
    public static InboxClaim Empty { get; } = new(Guid.Empty, []);

    /// <summary>Gets the number of rows claimed.</summary>
    public int Count => Messages.Count;
}
