namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// <see cref="ModelBuilder"/> extensions for applying messaging entity configurations.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies <see cref="OutboxMessageConfiguration"/> and <see cref="InboxMessageConfiguration"/>
    /// to the model. Call this from <c>OnModelCreating</c> in the application's
    /// <see cref="DbContext"/>.
    /// </summary>
    /// <param name="builder">The model builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ModelBuilder ApplyMessagingConfiguration(this ModelBuilder builder)
    {
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.ApplyConfiguration(new InboxMessageConfiguration());
        return builder;
    }
}
