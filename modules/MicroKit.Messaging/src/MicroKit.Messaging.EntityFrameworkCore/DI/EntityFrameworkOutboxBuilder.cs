using System.Diagnostics.CodeAnalysis;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// Configures one EF Core store, handed to the lambda of
/// <see cref="MessagingBuilderExtensions.UseEntityFrameworkOutbox{TContext}"/>.
/// </summary>
public sealed class EntityFrameworkOutboxBuilder
{
    private readonly MessageStoreBuilder _store;

    internal EntityFrameworkOutboxBuilder(MessageStoreBuilder store) => _store = store;

    /// <summary>Gets the store's key, as <see cref="EntityFrameworkOutbox.KeyOf{TContext}"/> returns it.</summary>
    public MessageStoreKey Key => _store.Key;

    /// <summary>
    /// Binds the assembly declaring <typeparamref name="TMarker"/> to this store: its handlers keep
    /// their inbox rows here, and its message types published outside any delivery are enlisted
    /// here. Needed only when several stores are configured; repeatable.
    /// </summary>
    /// <typeparam name="TMarker">Any type of the assembly to bind.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>An assembly bound to two stores fails the host start.</remarks>
    public EntityFrameworkOutboxBuilder ForAssemblyContaining<TMarker>()
    {
        _store.BindAssembly(typeof(TMarker).Assembly);
        return this;
    }

    /// <summary>
    /// Makes this store partitioned (#191): the engine drains the databases <typeparamref name="TSource"/>
    /// supplies at runtime — one per tenant, one per shard — each in scopes
    /// <typeparamref name="TInitializer"/> prepares, typically by pointing the scope's connection at the
    /// instance's database before the context is built.
    /// </summary>
    /// <typeparam name="TSource">The instance source, a keyed singleton.</typeparam>
    /// <typeparam name="TInitializer">The instance-scope initializer, keyed and scoped.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// The context's model is checked when the first instance starts, in its prepared scope, rather
    /// than at host start. No table is created: a source declares a database once it is migrated.
    /// </remarks>
    public EntityFrameworkOutboxBuilder PartitionedBy<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInitializer>()
        where TSource : class, IMessageStoreInstanceSource
        where TInitializer : class, IMessageStoreInstanceInitializer
    {
        _store.PartitionedBy<TSource, TInitializer>();
        return this;
    }
}
