namespace MicroKit.Messaging.Spi;

/// <summary>
/// What an inbox claim leaves out (#210): the consumers in consumer-wide back-off, and the
/// (consumer, tenant) pairs in back-off, on the store's inbox.
/// </summary>
/// <remarks>
/// <para>
/// Built by the engine once per retry batch. While nothing is in back-off the engine passes
/// <see cref="None"/>, shared, so a quiet store allocates nothing.
/// </para>
/// <para>
/// A row with no tenant (<see cref="InboxMessage.TenantId"/> <see langword="null"/>) is never left out, by
/// either list. A consumer in <see cref="ConsumerTypes"/> leaves out each of its rows that has a tenant; a
/// pair in <see cref="Pairs"/> leaves out that consumer's rows of that tenant only. Both compare
/// <see cref="InboxMessage.ConsumerType"/> and <see cref="InboxMessage.TenantId"/> as the store compares its
/// columns: under a case-insensitive collation, case-insensitively.
/// </para>
/// <para>
/// A store may match a pair on one value, the consumer key and the tenant id joined by U+001F (unit
/// separator), as the EF store does. That match is exact because no consumer key contains U+001F:
/// <see cref="MessageConsumerKey.Of"/> and <see cref="LocalConsumer"/> refuse one, so the first separator of
/// the joined value is at the consumer key's length. A row written by hand with a <c>ConsumerType</c>
/// containing U+001F names no registered consumer — the retry loop dead-letters it on sight — and can only
/// be left out of the claim while a pair whose joined value equals its own is held.
/// </para>
/// </remarks>
public sealed class InboxClaimExclusions
{
    /// <summary>Initializes a new <see cref="InboxClaimExclusions"/>.</summary>
    /// <param name="consumerTypes">The consumers in consumer-wide back-off, each an <see cref="InboxMessage.ConsumerType"/>.</param>
    /// <param name="pairs">The (consumer, tenant) pairs in back-off.</param>
    /// <exception cref="ArgumentNullException">A list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A consumer type or a tenant id is <see langword="null"/> or empty.</exception>
    /// <remarks>The lists are kept as given: a caller that changes one afterwards changes what is left out.</remarks>
    public InboxClaimExclusions(
        IReadOnlyList<string> consumerTypes, IReadOnlyList<(string ConsumerType, string TenantId)> pairs)
    {
        ArgumentNullException.ThrowIfNull(consumerTypes);
        ArgumentNullException.ThrowIfNull(pairs);
        for (var i = 0; i < consumerTypes.Count; i++)
        {
            if (string.IsNullOrEmpty(consumerTypes[i]))
            {
                throw new ArgumentException($"Consumer type {i} is null or empty: a row's ConsumerType never is.", nameof(consumerTypes));
            }
        }

        for (var i = 0; i < pairs.Count; i++)
        {
            if (string.IsNullOrEmpty(pairs[i].ConsumerType) || string.IsNullOrEmpty(pairs[i].TenantId))
            {
                throw new ArgumentException(
                    $"Pair {i} has a null or empty consumer type or tenant id: a row with no tenant is never left out.",
                    nameof(pairs));
            }
        }

        ConsumerTypes = consumerTypes;
        Pairs = pairs;
    }

    /// <summary>Gets the shared value that leaves nothing out: what a store with nothing in back-off is passed.</summary>
    public static InboxClaimExclusions None { get; } = new([], []);

    /// <summary>Gets the consumers whose rows with a tenant are left out.</summary>
    public IReadOnlyList<string> ConsumerTypes { get; }

    /// <summary>Gets the (consumer, tenant) pairs whose rows are left out.</summary>
    public IReadOnlyList<(string ConsumerType, string TenantId)> Pairs { get; }

    /// <summary>Gets a value indicating whether nothing is left out: both lists, as they are now, are empty.</summary>
    public bool IsEmpty => ConsumerTypes.Count == 0 && Pairs.Count == 0;
}
