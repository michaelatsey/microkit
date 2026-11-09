using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Builds primary-key lookups from the EF Core model, shared by the read and write repositories,
/// and resolves the services <c>UseMicroKitPersistence()</c> adds to a context.
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

    /// <summary>
    /// The context's <see cref="AggregateLoadRecord"/>, or <see langword="null"/> when the context was
    /// not configured with <c>UseMicroKitPersistence()</c>.
    /// </summary>
    internal static AggregateLoadRecord? FindLoadRecord(DbContext context) =>
        ((IInfrastructure<IServiceProvider>)context).Instance.GetService<AggregateLoadRecord>();

    /// <summary>The context's <see cref="AggregateLoadRecord"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The context was not configured with <c>UseMicroKitPersistence()</c>.
    /// </exception>
    internal static AggregateLoadRecord LoadRecordOf(DbContext context) =>
        FindLoadRecord(context)
        ?? throw new InvalidOperationException(
            $"'{context.GetType().Name}' is not configured for MicroKit.Persistence: the write-side repository cannot " +
            "tell a whole aggregate from a partial one. Call UseMicroKitPersistence() on its DbContextOptionsBuilder, " +
            "or register it through AddDbContext, UsePostgreSQL or UseSqlServer on the EfCoreBuilder.");

    /// <summary>
    /// Checks that every non-null value of <paramref name="keyValues"/> is of its key property's CLR
    /// type, as <c>DbSet.Find</c> does: a converted key takes its model type, never the column's.
    /// </summary>
    /// <exception cref="ArgumentException">A key value is of another type.</exception>
    internal static void EnsureKeyValueTypes(IEntityType entityType, IReadOnlyList<IProperty> keyProperties, object?[] keyValues)
    {
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var expected = Nullable.GetUnderlyingType(keyProperties[i].ClrType) ?? keyProperties[i].ClrType;
            if (keyValues[i] is { } value && !expected.IsInstanceOfType(value))
            {
                throw new ArgumentException(
                    $"Key value {i} of '{entityType.ClrType.Name}' must be a '{expected.Name}' ({keyProperties[i].Name}); " +
                    $"a '{value.GetType().Name}' was given.",
                    nameof(keyValues));
            }
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
    internal static Expression KeyEquals(Expression property, Expression value)
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
