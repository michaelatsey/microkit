using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Builds primary-key lookups from the EF Core model, shared by the read and write repositories.
/// </summary>
internal static class KeyLookup
{
    private static readonly ConcurrentDictionary<Type, bool> s_equalityOperatorSupport = new();

    private static readonly MethodInfo ObjectEqualsMethod =
        typeof(object).GetMethod(nameof(Equals), [typeof(object)])!;

    private static readonly MethodInfo EfPropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly FieldInfo KeyValuesField =
        typeof(KeyValuesHolder).GetField(nameof(KeyValuesHolder.Values))!;

    /// <summary>
    /// The entry the change tracker holds for <paramref name="keyValues"/>, or <see langword="null"/>.
    /// Runs with <see cref="ChangeTracker.AutoDetectChangesEnabled"/> off — reading
    /// <see cref="DbSet{TEntity}.Local"/> otherwise runs a full <c>DetectChanges</c> over every tracked
    /// entity — and restores the caller's setting.
    /// </summary>
    internal static EntityEntry<TEntity>? FindTracked<TEntity>(DbContext context, DbSet<TEntity> set, object[] keyValues)
        where TEntity : class
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        try
        {
            tracker.AutoDetectChangesEnabled = false;
            return set.Local.FindEntryUntyped(keyValues);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <summary>The primary key properties of <paramref name="entityType"/>, in key order.</summary>
    /// <exception cref="InvalidOperationException">The entity type has no primary key.</exception>
    internal static IReadOnlyList<IProperty> PrimaryKeyOf(IEntityType entityType) =>
        entityType.FindPrimaryKey()?.Properties
        ?? throw new InvalidOperationException($"'{entityType.ClrType.Name}' has no primary key.");

    /// <summary>
    /// <c>e =&gt; EF.Property&lt;T0&gt;(e, "K0") == (T0)values[0] &amp;&amp; …</c>. The values are read through a
    /// closure object, so EF Core parameterises them and every call shares one compiled query.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming", "IL2060:MakeGenericMethod",
        Justification = "EF.Property<T> carries no trimming annotation and is only ever translated by EF Core, which is not trim-safe itself (see IL2091 in the project file).")]
    internal static Expression<Func<TEntity, bool>> Predicate<TEntity>(
        IReadOnlyList<IProperty> keyProperties,
        object[] keyValues)
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var values = Expression.Field(Expression.Constant(new KeyValuesHolder(keyValues)), KeyValuesField);

        Expression? body = null;
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var clrType = keyProperties[i].ClrType;
            var property = Expression.Call(
                EfPropertyMethod.MakeGenericMethod(clrType),
                entity,
                Expression.Constant(keyProperties[i].Name));
            var value = Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(i)), clrType);
            var equal = KeyEquals(property, value);
            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        return Expression.Lambda<Func<TEntity, bool>>(body!, entity);
    }

    // == where the key type supports it (primitives, Guid, records…); otherwise value equality
    // through Equals(object), which EF Core translates to the same SQL comparison — DbSet.Find
    // accepts such keys, so the repositories must too. Probed once per key type.
    private static Expression KeyEquals(Expression property, Expression value)
    {
        var supportsEqualityOperator = s_equalityOperatorSupport.GetOrAdd(property.Type, static type =>
        {
            try
            {
                _ = Expression.Equal(Expression.Default(type), Expression.Default(type));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });

        return supportsEqualityOperator
            ? Expression.Equal(property, value)
            : Expression.Call(property, ObjectEqualsMethod, Expression.Convert(value, typeof(object)));
    }

    private sealed class KeyValuesHolder(object[] values)
    {
        public readonly object[] Values = values;
    }
}
