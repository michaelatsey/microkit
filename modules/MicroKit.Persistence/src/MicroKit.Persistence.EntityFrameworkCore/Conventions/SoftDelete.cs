namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Soft delete of whole aggregates: an aggregate whose root is configured with
/// <see cref="SoftDeleteBuilderExtensions.HasSoftDelete(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder)"/>
/// is never deleted from the database by a save (bulk <c>ExecuteDelete</c> and raw SQL still delete).
/// Deleting its root marks every entity of the aggregate instead, and a named query filter hides the marked
/// rows. It needs <c>UseMicroKitPersistence()</c> on the context.
/// </summary>
/// <remarks>
/// <para>
/// Every entity type of a soft-deletable aggregate gets the shadow <see cref="DateTimeOffset"/>? property
/// <see cref="PropertyName"/> and the named query filter <see cref="FilterName"/> (on the root type of its
/// hierarchy, where EF Core accepts a filter); an owned type follows its owner. A save that deletes the root
/// turns every delete of the aggregate into an update that sets the mark: one UTC instant per save, from
/// the application's <see cref="TimeProvider"/>.
/// </para>
/// <para>
/// The property name is reserved on those types: model building fails when one maps it to a CLR member,
/// gives it another type, or maps another property of its table to its column. Its column may be configured
/// like any other, and a naming convention applies to it.
/// </para>
/// <para>
/// A read through <c>QueryOptions.WithDeleted()</c> ignores <see cref="FilterName"/> only; every other query
/// filter still applies. Read the mark in a projection through <c>EF.Property</c>:
/// <code>
/// .Select(o =&gt; new OrderDto(o.Id, EF.Property&lt;DateTimeOffset?&gt;(o, SoftDelete.PropertyName)))
/// </code>
/// </para>
/// </remarks>
public static class SoftDelete
{
    /// <summary>
    /// The name of the shadow property that holds the deletion mark — a UTC instant, offset zero —;
    /// <see langword="null"/> while the entity is live.
    /// </summary>
    public const string PropertyName = "AggregateDeletedAt";

    /// <summary>The name of the query filter that hides soft-deleted rows.</summary>
    public const string FilterName = "MicroKit.SoftDelete";

    /// <summary>The model annotation <c>HasSoftDelete()</c> sets on the aggregate root.</summary>
    internal const string Annotation = "MicroKit:SoftDelete";
}
