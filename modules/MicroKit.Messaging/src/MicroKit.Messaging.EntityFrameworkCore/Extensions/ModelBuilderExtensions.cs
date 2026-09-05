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
    /// <b>Supported on PostgreSQL and SQLite. NOT supported on SQL Server.</b> This is a narrower
    /// claim than the rest of the module makes, and it is stated here because this method is the
    /// one call through which a consumer materializes the schema.
    /// </para>
    /// <para>
    /// The reason is a single index. <see cref="OutboxMessageConfiguration"/> declares
    /// <c>UX_OutboxMessages_Origin_ContractName</c>, unique over
    /// (<see cref="OutboxMessage.OriginMessageId"/>, <see cref="OutboxMessage.ContractName"/>), and
    /// every <see cref="MessageKind.Notification"/> row carries <see langword="null"/> in both. On
    /// PostgreSQL and SQLite nulls are distinct in a unique index, so unlimited such rows coexist.
    /// SQL Server compares them as equal, so at most one <c>(NULL, NULL)</c> row can exist and the
    /// <b>second</b> notification row the application ever writes is rejected — not at DDL time and
    /// not on the first row, but on the second insert, in production.
    /// </para>
    /// <para>
    /// That index is a model invariant rather than a performance hint: it is what stops a
    /// redelivered dispatch from producing a duplicate integration message, so it cannot simply be
    /// dropped on an unsupported provider. A filtered index (<c>WHERE</c> both columns
    /// <c>IS NOT NULL</c>) restores the behaviour on SQL Server and is deliberately not shipped —
    /// <c>HasFilter</c> takes provider-specific SQL and this is the provider-neutral package. It is
    /// a workaround a consumer owns, not a supported configuration. See the module CHANGELOG.
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
