namespace MicroKit.Messaging.Spi;

/// <summary>
/// One instance of a partitioned message store: a unit of persistence the engine drains — one
/// tenant's database, one shard — supplied at runtime by the store's
/// <see cref="IMessageStoreInstanceSource"/> (#191).
/// </summary>
/// <param name="Id">The instance's id, unique within its store. Opaque: its format belongs to the source.</param>
/// <remarks>
/// <para>
/// Part of the <b>store SPI</b>. A store is what a host configures (<c>AddMessageStore</c>, its
/// <see cref="MessageStoreKey"/>, its keyed services); an instance is one database of a
/// partitioned store. The instance is not a tenant, and names no technology.
/// </para>
/// <para>Two instances are equal when their ids are equal, ordinally.</para>
/// </remarks>
public sealed record MessageStoreInstance(string Id)
{
    /// <summary>Gets the instance's id.</summary>
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id)
        ? Id
        : throw new ArgumentException("A store instance id cannot be empty or whitespace.", nameof(Id));

    /// <inheritdoc />
    public override string ToString() => Id;
}
