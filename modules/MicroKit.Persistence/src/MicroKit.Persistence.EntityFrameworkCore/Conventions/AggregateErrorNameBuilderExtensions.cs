using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Model-building extensions that name an aggregate root in the codes of <see cref="PersistenceErrors"/>.
/// </summary>
public static class AggregateErrorNameBuilderExtensions
{
    /// <summary>
    /// Fixes the error name of an aggregate root: the <c>&lt;ROOT&gt;</c> segment of the codes
    /// <c>PERSISTENCE.&lt;ROOT&gt;.&lt;REASON&gt;</c> that the <see cref="IResultUnitOfWork"/> path returns.
    /// </summary>
    /// <param name="entityTypeBuilder">The builder of the aggregate root, the top-most root type of its hierarchy.</param>
    /// <param name="errorName">The name, in UPPER_SNAKE (<see cref="PersistenceErrors.IsValidErrorName"/>).</param>
    /// <returns><paramref name="entityTypeBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// By default a root's error name is its simple CLR name in UPPER_SNAKE
    /// (<see cref="PersistenceErrors.DefaultErrorName"/>): <c>Order</c> → <c>ORDER</c>. Model building fails
    /// when two roots resolve to the same name — two <c>Order</c> classes in different namespaces — and
    /// when this name is set on a type that is not a top-most aggregate root. Rename one of the roots here:
    /// <code>
    /// modelBuilder.Entity&lt;Billing.Order&gt;().HasAggregateErrorName("BILLING_ORDER");
    /// </code>
    /// </para>
    /// <para>
    /// It sets the model annotation <c>MicroKit:ErrorName</c>. The in-memory double of
    /// <c>MicroKit.Persistence.Testing</c> has no model and uses the default name.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypeBuilder"/> or <paramref name="errorName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errorName"/> is not a valid error name.</exception>
    public static EntityTypeBuilder HasAggregateErrorName(this EntityTypeBuilder entityTypeBuilder, string errorName)
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(errorName);
        if (!PersistenceErrors.IsValidErrorName(errorName))
        {
            throw new ArgumentException(
                $"'{errorName}' is not a valid error name: use UPPER_SNAKE (A–Z, 0–9, single underscores), starting with a letter.",
                nameof(errorName));
        }

        entityTypeBuilder.HasAnnotation(AggregateErrorNameConvention.ErrorNameAnnotation, errorName);
        return entityTypeBuilder;
    }

    /// <inheritdoc cref="HasAggregateErrorName(EntityTypeBuilder, string)"/>
    /// <typeparam name="TEntity">The aggregate root type.</typeparam>
    public static EntityTypeBuilder<TEntity> HasAggregateErrorName<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string errorName)
        where TEntity : class =>
        (EntityTypeBuilder<TEntity>)HasAggregateErrorName((EntityTypeBuilder)entityTypeBuilder, errorName);
}
