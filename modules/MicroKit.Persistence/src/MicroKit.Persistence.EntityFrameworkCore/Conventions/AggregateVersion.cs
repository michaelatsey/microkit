namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The version of an aggregate: a shadow <see langword="long"/> concurrency token that
/// <c>UseMicroKitPersistence()</c> adds to every aggregate root and bumps on every save that writes
/// any entity of the aggregate.
/// </summary>
/// <remarks>
/// <para>
/// The property name is reserved on aggregate roots: model building fails when a root maps it to a
/// CLR member, changes its type from <see langword="long"/>, stops it being a concurrency token, or
/// maps another property of the root's table to its column.
/// Its column may be configured like any other —
/// <c>Property&lt;long&gt;(AggregateVersion.PropertyName).HasColumnName("row_version")</c> — and a naming
/// convention applies to it.
/// </para>
/// <para>
/// Read it in a projection through <c>EF.Property</c>:
/// <code>
/// .Select(o =&gt; new OrderDto(o.Id, EF.Property&lt;long&gt;(o, AggregateVersion.PropertyName)))
/// </code>
/// On a loaded aggregate, use <see cref="IRepository{TAggregate, TId}.GetVersion"/>.
/// </para>
/// </remarks>
public static class AggregateVersion
{
    /// <summary>The name of the shadow property that holds an aggregate root's version.</summary>
    public const string PropertyName = "AggregateVersion";
}
