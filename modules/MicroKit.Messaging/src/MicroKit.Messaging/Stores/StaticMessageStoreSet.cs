namespace MicroKit.Messaging.Stores;

/// <summary>The static store set: every store registered through <c>AddMessageStore</c>, fixed at start.</summary>
internal sealed class StaticMessageStoreSet(MessagingComposition composition) : IMessageStoreSet
{
    private readonly MessageStoreKey[] _stores = [.. composition.Stores.Select(s => s.Key)];

    /// <inheritdoc />
    public IReadOnlyCollection<MessageStoreKey> GetStores() => _stores;
}
