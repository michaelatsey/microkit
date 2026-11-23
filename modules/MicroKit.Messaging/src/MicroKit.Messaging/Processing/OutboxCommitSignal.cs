using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// Wakes the outbox loop that drains a commit's rows (#215): a single store's loop, or the loop of
/// the partitioned store's instance this scope serves.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, built at a store provider's first enlistment. It captures the scope's
/// <see cref="DeliveryStoreHolder"/> then and reads it when called, at the end of the scope, when
/// nothing can be resolved any more. It holds nothing disposable.
/// </para>
/// <para>
/// <b>What is woken.</b> In a scope the engine prepared for an instance of this store, that instance;
/// otherwise this store's loop if it is single. Nothing for a partitioned store from a host scope or
/// from another store's delivery scope, or for an unknown key: their rows wait for the next poll. An
/// instance's wake after its removal is written into a channel nothing reads, and creates no state.
/// The source's <c>TryGetInstance</c> is never called: it maps a delivery, which a publication is not.
/// </para>
/// </remarks>
internal sealed class OutboxCommitSignal(DeliveryStoreHolder holder, OutboxWakes wakes) : IOutboxCommitSignal
{
    /// <inheritdoc />
    public void Committed(MessageStoreKey store)
    {
        if (store is null)
        {
            return;
        }

        if (holder.Instance is { } instance && store.Equals(holder.Store))
        {
            instance.OutboxWake.Signal();
            return;
        }

        wakes.Signal(store);
    }
}
