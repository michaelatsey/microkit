using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>Model-building extensions that make an aggregate soft-deletable (<see cref="SoftDelete"/>).</summary>
public static class SoftDeleteBuilderExtensions
{
    /// <summary>
    /// Makes the aggregate of this root soft-deletable: deleting the root marks every entity of the aggregate
    /// with <see cref="SoftDelete.PropertyName"/> instead of deleting it, and the query filter
    /// <see cref="SoftDelete.FilterName"/> hides the marked rows.
    /// </summary>
    /// <param name="entityTypeBuilder">The builder of the aggregate root, the top-most root type of its hierarchy.</param>
    /// <returns><paramref name="entityTypeBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Model building fails when it is set on a type that is not a top-most aggregate root; when an entity of
    /// the aggregate carries an anonymous query filter (EF Core cannot combine it with a named one: name it);
    /// and when a relationship inside the aggregate is required and does not cascade (<c>Restrict</c>,
    /// <c>NoAction</c>, <c>ClientSetNull</c>, <c>SetNull</c>): EF Core would reject the root's removal before
    /// the save could turn it into a soft delete.
    /// </para>
    /// <para>
    /// A foreign key into the aggregate from outside it (another aggregate, an entity outside every boundary,
    /// the join rows of a many-to-many marked <c>IsOutsideAggregate()</c>) is <c>ClientNoAction</c>: EF Core
    /// leaves a tracked dependent alone when the root is removed; an explicit other behaviour fails model
    /// building.
    /// </para>
    /// <para>
    /// At save time, deleting a root its repository did not load whole fails with
    /// <see cref="InvalidOperationException"/>: its untracked entities could not be marked. A child removed
    /// from an aggregate that stays alive is still deleted for real.
    /// </para>
    /// <para>
    /// It needs <c>UseMicroKitPersistence()</c> on the context: without it the annotation does nothing and a
    /// delete is real. It is a schema change: a nullable column on the table of every entity type of the
    /// aggregate, which needs a migration. A unique index still sees soft-deleted rows; filter it on the live
    /// ones (<c>HasFilter("\"AggregateDeletedAt\" IS NULL")</c>). It sets the model annotation
    /// <c>MicroKit:SoftDelete</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypeBuilder"/> is <see langword="null"/>.</exception>
    public static EntityTypeBuilder HasSoftDelete(this EntityTypeBuilder entityTypeBuilder)
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        entityTypeBuilder.HasAnnotation(SoftDelete.Annotation, true);
        return entityTypeBuilder;
    }

    /// <inheritdoc cref="HasSoftDelete(EntityTypeBuilder)"/>
    /// <typeparam name="TEntity">The aggregate root type.</typeparam>
    public static EntityTypeBuilder<TEntity> HasSoftDelete<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder)
        where TEntity : class =>
        (EntityTypeBuilder<TEntity>)HasSoftDelete((EntityTypeBuilder)entityTypeBuilder);
}
