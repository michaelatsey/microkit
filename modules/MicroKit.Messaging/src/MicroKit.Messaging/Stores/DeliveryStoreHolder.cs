namespace MicroKit.Messaging.Stores;

/// <summary>
/// Names the store — and, for a partitioned store, the instance (#191) — of the delivery or dispatch
/// running in this scope. Set by the engine in every scope it opens for a store; <see langword="null"/>
/// in any other scope.
/// </summary>
/// <remarks>
/// Scoped, and written by the engine on the scope it received rather than by the scope factory: a
/// host may supply its own <see cref="IExecutionScopeFactory"/>, and the store must not depend on
/// it carrying a value through.
/// </remarks>
internal sealed class DeliveryStoreHolder
{
    /// <summary>Gets or sets the store of the scope's delivery, or null outside one.</summary>
    public MessageStoreKey? Store { get; set; }

    /// <summary>
    /// Gets or sets the instance of a partitioned store this scope serves (#191), or null: a single
    /// store's scope, or a scope outside the engine. Written before the instance initializer runs.
    /// </summary>
    public Processing.StoreInstanceState? Instance { get; set; }
}
