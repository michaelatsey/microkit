namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>The store keys of the EF Core outboxes.</summary>
public static class EntityFrameworkOutbox
{
    /// <summary>
    /// Gets the key of the store configured by
    /// <see cref="MessagingBuilderExtensions.UseEntityFrameworkOutbox{TContext}"/> for
    /// <typeparamref name="TContext"/>, to name it in <see cref="PublishOptions.Store"/>.
    /// </summary>
    /// <typeparam name="TContext">The store's <see cref="DbContext"/> type.</typeparam>
    /// <returns>The store key.</returns>
    public static MessageStoreKey KeyOf<TContext>()
        where TContext : DbContext
        => KeyCache<TContext>.Key;

    private static class KeyCache<TContext>
        where TContext : DbContext
    {
        public static readonly MessageStoreKey Key = new("ef:" + typeof(TContext).FullName);
    }
}
