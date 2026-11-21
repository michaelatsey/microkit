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
}
