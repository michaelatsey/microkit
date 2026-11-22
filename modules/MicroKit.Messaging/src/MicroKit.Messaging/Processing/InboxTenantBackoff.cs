using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The tenants in back-off on each store's inbox (#124): one <see cref="TenantBackoff"/> per store,
/// shared by the store's retry loop and by every transport delivery guarded by it.
/// </summary>
/// <remarks>
/// A singleton. Its curve is the inbox's: <see cref="InboxProcessorOptions.PollingInterval"/>,
/// doubling to <see cref="InboxProcessorOptions.DependencyUnavailableBackoff"/>. While no store has
/// anything in back-off, <see cref="IsQuiet"/> answers with one read, so a delivery pays no lookup.
/// Its entries are keyed by <see cref="StoreInstanceKey"/>, a store's with no instance.
/// </remarks>
/// <param name="options">The inbox options: the curve's base and ceiling.</param>
internal sealed class InboxTenantBackoff(InboxProcessorOptions options)
{
    private readonly ConcurrentDictionary<StoreInstanceKey, TenantBackoff> _stores = new();
    private readonly StrongBox<int> _loud = new();
    private readonly TimeSpan _initial = options.PollingInterval;
    private readonly TimeSpan _ceiling = options.DependencyUnavailableBackoff;

    /// <summary>Gets a value indicating whether no store has a tenant, a probe or a suspicion to clear.</summary>
    public bool IsQuiet => Volatile.Read(ref _loud.Value) == 0;

    /// <summary>
    /// Gets the stores whose state exists — each one from its first use — for the inbox's back-off
    /// up-down counter (#126). Safe to enumerate while stores are added.
    /// </summary>
    public IEnumerable<KeyValuePair<StoreInstanceKey, TenantBackoff>> Stores => _stores;

    /// <summary>The back-off state of <paramref name="store"/>'s inbox.</summary>
    /// <param name="store">The store.</param>
    /// <returns>Its state, created on first use.</returns>
    public TenantBackoff For(MessageStoreKey store)
        => _stores.GetOrAdd(
            new StoreInstanceKey(store, null),
            static (_, self) => new TenantBackoff(self._initial, self._ceiling, self._loud),
            this);
}
