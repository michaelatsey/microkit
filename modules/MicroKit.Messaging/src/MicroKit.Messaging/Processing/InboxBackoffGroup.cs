using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The inbox back-off of every store (#124, #210): one <see cref="InboxBackoff"/> per single store, and one
/// per instance of a partitioned store (#191), shared by its retry loop and by every transport delivery
/// guarded by it.
/// </summary>
/// <remarks>
/// A singleton. Its curve is the inbox's: <see cref="InboxProcessorOptions.PollingInterval"/>,
/// doubling to <see cref="InboxProcessorOptions.DependencyUnavailableBackoff"/>, for a (consumer, tenant)
/// pair and for a consumer alike. While no store has anything in back-off, <see cref="IsQuiet"/> answers with
/// one read, so a delivery pays no lookup. Its entries are keyed by <see cref="StoreInstanceKey"/>. A single
/// store's entry is created on first use (<see cref="For"/>); an instance's (#191) is created and released by
/// the engine alone (<see cref="Add"/>, <see cref="Release"/>), never by a delivery, which reaches it through
/// the instance it was mapped to.
/// </remarks>
/// <param name="options">The inbox options: the curve's base and ceiling.</param>
internal sealed class InboxBackoffGroup(InboxProcessorOptions options)
{
    private readonly ConcurrentDictionary<StoreInstanceKey, InboxBackoff> _stores = new();
    private readonly StrongBox<int> _loud = new();
    private readonly TimeSpan _initial = options.PollingInterval;
    private readonly TimeSpan _ceiling = options.DependencyUnavailableBackoff;

    /// <summary>
    /// Gets a value indicating whether no store has a pair or a consumer in back-off, a probe pending or a
    /// suspicion standing.
    /// </summary>
    public bool IsQuiet => Volatile.Read(ref _loud.Value) == 0;

    /// <summary>
    /// Gets the stores whose state exists — each one from its first use — for the inbox's back-off up-down
    /// counters (#126, #210). Safe to enumerate while stores are added.
    /// </summary>
    public IEnumerable<KeyValuePair<StoreInstanceKey, InboxBackoff>> Stores => _stores;

    /// <summary>The back-off state of <paramref name="store"/>'s inbox.</summary>
    /// <param name="store">The store.</param>
    /// <returns>Its state, created on first use.</returns>
    public InboxBackoff For(MessageStoreKey store)
        => _stores.GetOrAdd(
            new StoreInstanceKey(store, null),
            static (_, self) => new InboxBackoff(self._initial, self._ceiling, self._loud),
            this);

    /// <summary>Creates the back-off state of an instance's inbox, when the engine starts draining it.</summary>
    /// <param name="key">The instance; never a single store's key.</param>
    /// <returns>Its new state.</returns>
    /// <exception cref="InvalidOperationException">The instance's state exists: it was not released.</exception>
    public InboxBackoff Add(StoreInstanceKey key)
    {
        ArgumentNullException.ThrowIfNull(key.Instance);
        var state = new InboxBackoff(_initial, _ceiling, _loud);
        return _stores.TryAdd(key, state)
            ? state
            : throw new InvalidOperationException(
                $"Instance '{key.Instance}' of store '{key.Store}' already has inbox back-off state: an " +
                "instance is released before it is drained again.");
    }

    /// <summary>
    /// Releases the back-off state of an instance once its loops have ended: its entry leaves, and it
    /// stops counting toward <see cref="IsQuiet"/>. A delivery still holding it writes into a detached
    /// object that nothing reads.
    /// </summary>
    /// <param name="key">The instance.</param>
    public void Release(StoreInstanceKey key)
    {
        if (_stores.TryRemove(key, out var state))
        {
            state.Detach();
        }
    }
}
