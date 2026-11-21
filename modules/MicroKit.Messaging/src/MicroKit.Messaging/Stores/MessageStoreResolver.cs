using System.Reflection;

namespace MicroKit.Messaging.Stores;

/// <summary>
/// Chooses the store of a publication and of a handler, from the composition and the scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>A publication's store, first match wins:</b> <see cref="PublishOptions.Store"/>; inside a
/// scope the engine opened for a delivery, that delivery's store; the only configured store; the
/// store bound to the message type's assembly. Otherwise the publication fails — never a guess,
/// and never a direct send while stores are configured.
/// </para>
/// <para>
/// <b>A handler's store:</b> the only configured store, else the store bound to the handler type's
/// assembly. An unbound handler with several stores is a start failure
/// (<see cref="MessagingCompositionAnalysis"/>), so a miss here is a container built without one.
/// </para>
/// <para>
/// Only the store's <i>key</i> is chosen here. Its services — the <c>DbContext</c> behind them
/// included — are resolved from the scope, keyed by it.
/// </para>
/// </remarks>
internal sealed class MessageStoreResolver
{
    private readonly IMessageStoreSet _storeSet;
    private readonly Dictionary<Assembly, MessageStoreKey> _bindings = [];

    /// <summary>Initializes the resolver over the composition's bindings and the engine's store set.</summary>
    /// <param name="composition">The composition, which holds the assembly bindings.</param>
    /// <param name="storeSet">The configured stores, read on every resolution.</param>
    public MessageStoreResolver(MessagingComposition composition, IMessageStoreSet storeSet)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(storeSet);
        _storeSet = storeSet;

        foreach (var store in composition.Stores)
        {
            foreach (var assembly in store.Assemblies)
            {
                // An assembly bound to two stores is a start failure; the first binding stands
                // meanwhile.
                _bindings.TryAdd(assembly, store.Key);
            }
        }
    }

    /// <summary>The store a publication is enlisted in, or null when no store is configured.</summary>
    /// <param name="messageType">The published type.</param>
    /// <param name="explicitStore">The store named by <see cref="PublishOptions.Store"/>, if any.</param>
    /// <param name="deliveryStore">The store of the delivery running in the scope, if any.</param>
    /// <returns>The store's key, or <see langword="null"/> to send straight to the transport.</returns>
    /// <exception cref="IntegrationEventPublishException">The explicit store is not allowed here.</exception>
    /// <exception cref="IntegrationEventConfigurationException">No rule chooses a store.</exception>
    public MessageStoreKey? ForPublication(
        Type messageType, MessageStoreKey? explicitStore, MessageStoreKey? deliveryStore)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        var stores = _storeSet.GetStores();

        if (stores.Count == 0)
        {
            return explicitStore is null
                ? null
                : throw new IntegrationEventPublishException(
                    $"'{messageType.Name}' was published to store '{explicitStore}', but no store is " +
                    "configured: the bus sends straight to the transport. Remove PublishOptions.Store, " +
                    "or configure the store. Nothing was sent.");
        }

        if (explicitStore is not null)
        {
            if (!stores.Contains(explicitStore))
            {
                throw new IntegrationEventPublishException(
                    $"'{messageType.Name}' was published to store '{explicitStore}', which is not " +
                    $"configured. The configured stores are {Names(stores)}. Nothing was enlisted.");
            }

            if (deliveryStore is not null && explicitStore != deliveryStore)
            {
                throw new IntegrationEventPublishException(
                    $"'{messageType.Name}' was published to store '{explicitStore}' from a delivery " +
                    $"of store '{deliveryStore}'. A handler's publications commit with its own work, " +
                    "so they go to its delivery's store; an explicit store may only name that one. " +
                    "Nothing was enlisted.");
            }

            return explicitStore;
        }

        if (deliveryStore is not null)
        {
            return deliveryStore;
        }

        if (stores.Count == 1)
        {
            return stores.First();
        }

        if (_bindings.TryGetValue(messageType.Assembly, out var bound))
        {
            return bound;
        }

        throw new IntegrationEventConfigurationException(
            $"'{messageType.FullName}' was published outside any delivery, and {stores.Count} " +
            $"stores are configured ({Names(stores)}), none bound to its assembly " +
            $"'{messageType.Assembly.GetName().Name}'. Bind that assembly to one store " +
            "(for EF Core: store => store.ForAssemblyContaining<T>()), or name the store with " +
            "PublishOptions.Store. Nothing was enlisted.");
    }

    /// <summary>The store guarding <paramref name="handlerType"/>, or null when no store is configured.</summary>
    /// <param name="handlerType">The handler type.</param>
    /// <returns>The store's key, or <see langword="null"/>.</returns>
    /// <exception cref="IntegrationEventConfigurationException">Several stores, and none bound to the handler.</exception>
    public MessageStoreKey? ForHandler(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        var stores = _storeSet.GetStores();

        if (stores.Count == 0)
        {
            return null;
        }

        if (stores.Count == 1)
        {
            return stores.First();
        }

        return _bindings.TryGetValue(handlerType.Assembly, out var bound)
            ? bound
            : throw new IntegrationEventConfigurationException(
                $"Handler '{handlerType.FullName}' is bound to no store, and {stores.Count} are " +
                $"configured ({Names(stores)}). Bind its assembly '{handlerType.Assembly.GetName().Name}' " +
                "to one store.");
    }

    private static string Names(IEnumerable<MessageStoreKey> stores) => string.Join(", ", stores.Select(s => $"'{s}'"));
}
