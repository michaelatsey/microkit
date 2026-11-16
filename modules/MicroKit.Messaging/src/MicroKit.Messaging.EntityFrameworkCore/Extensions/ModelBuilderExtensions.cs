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
    /// <remarks>
    /// <para>
    /// This is the only model hook of the module: a store configured with
    /// <c>bus.UseEntityFrameworkOutbox&lt;TContext&gt;()</c> needs it in that context's model, and
    /// the host fails to start without it. The host owns the migrations: none is shipped.
    /// </para>
    /// <para>
    /// The module's suites run on PostgreSQL and SQLite. The replay index that excluded SQL Server
    /// is gone (#188); no suite runs SQL Server.
    /// </para>
    /// </remarks>
    /// <param name="builder">The model builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ModelBuilder ApplyMessagingConfiguration(this ModelBuilder builder)
    {
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.ApplyConfiguration(new InboxMessageConfiguration());
        return builder;
    }
}
