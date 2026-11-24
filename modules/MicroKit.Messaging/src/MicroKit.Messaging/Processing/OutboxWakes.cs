using System.Collections.Frozen;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The wake of each single store's outbox loop (#215), created once with the engine's state: a
/// partitioned store's are its instances' (<see cref="StoreInstanceState.OutboxWake"/>).
/// </summary>
/// <remarks>
/// A frozen map, built from the configured stores the first time it is resolved, and read without
/// resolving anything: a commit is reported while its scope is disposed, when nothing can be resolved
/// from it, and after host stop the root provider is disposed too.
/// </remarks>
internal sealed class OutboxWakes
{
    private readonly FrozenDictionary<MessageStoreKey, LoopWake> _single;

    /// <summary>Initializes a new <see cref="OutboxWakes"/>.</summary>
    /// <param name="stores">The configured stores.</param>
    /// <param name="instances">Which of them are partitioned.</param>
    public OutboxWakes(IMessageStoreSet stores, StoreInstanceRegistry instances)
    {
        var single = new Dictionary<MessageStoreKey, LoopWake>();
        foreach (var store in stores.GetStores())
        {
            if (!instances.IsPartitioned(store))
            {
                single[store] = new LoopWake();
            }
        }

        _single = single.ToFrozenDictionary();
    }

    /// <summary>Gets the wake of a single store's outbox loop; null for any other key.</summary>
    /// <param name="store">The store.</param>
    /// <returns>Its wake, or null.</returns>
    public LoopWake? Of(MessageStoreKey store) => _single.GetValueOrDefault(store);

    /// <summary>Wakes a single store's outbox loop; any other key does nothing. Never throws.</summary>
    /// <param name="store">The store.</param>
    public void Signal(MessageStoreKey store)
    {
        if (_single.TryGetValue(store, out var wake))
        {
            wake.Signal();
        }
    }
}
