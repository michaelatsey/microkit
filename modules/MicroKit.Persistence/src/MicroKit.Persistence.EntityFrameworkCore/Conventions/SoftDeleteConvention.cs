using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Gives every entity type of an aggregate whose root has <c>HasSoftDelete()</c> the shadow
/// <see cref="SoftDelete.PropertyName"/> and the named query filter <see cref="SoftDelete.FilterName"/>,
/// on the root type of each hierarchy; owned types follow their owner.
/// </summary>
/// <remarks>
/// It runs among the first finalizing conventions, after <see cref="AggregateVersionConvention"/>, so the
/// property it adds goes through the model's naming conventions. It adds a named filter and keeps every
/// named filter already there. Model building fails on: the annotation on a type that is not a top-most
/// root; an anonymous filter on an entity of the aggregate; a required foreign key between two types of the
/// aggregate configured <c>Restrict</c>, <c>NoAction</c>, <c>ClientSetNull</c> or <c>SetNull</c>, whether or not a
/// navigation reaches it; and a misuse of the reserved property name.
/// </remarks>
internal sealed class SoftDeleteConvention : IModelFinalizingConvention
{
    private static readonly System.Reflection.MethodInfo s_property =
        typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(DateTimeOffset?));

    /// <summary>Gets a value indicating whether the aggregate of this top-most root is soft-deletable.</summary>
    internal static bool IsSoftDeletable(IReadOnlyEntityType root) => root.FindAnnotation(SoftDelete.Annotation)?.Value is true;

    /// <inheritdoc/>
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var model = modelBuilder.Metadata;

        foreach (var type in model.GetEntityTypes().ToList())
        {
            if (IsSoftDeletable(type) && !AggregateBoundaryConvention.IsTopMostRoot(type))
            {
                throw new InvalidOperationException(
                    $"'{type.DisplayName()}' is configured with HasSoftDelete(), but it is not the top-most aggregate root of " +
                    "its hierarchy: soft delete applies to a whole aggregate. Call HasSoftDelete() on the top-most root.");
            }
        }

        foreach (var root in model.GetEntityTypes().Where(t => AggregateBoundaryConvention.IsTopMostRoot(t) && IsSoftDeletable(t)).ToList())
        {
            // The root type of the root's own hierarchy: a mapped base type that is no aggregate root carries it.
            var hierarchies = new List<IConventionEntityType> { (IConventionEntityType)root.GetRootType() };
            var members = new HashSet<IReadOnlyEntityType>(root.GetDerivedTypesInclusive(), ReferenceEqualityComparer.Instance);
            AggregateIncludePlan.Walk(root, step =>
            {
                if (!step.Target.IsOwned())
                    AddOnce(hierarchies, (IConventionEntityType)step.Target.GetRootType());
                members.UnionWith(step.Target.GetDerivedTypesInclusive());
            });

            EnsureCascades(root, members);
            foreach (var hierarchy in hierarchies)
                Mark(model, root, hierarchy, hierarchies);
        }
    }

    private static void AddOnce(List<IConventionEntityType> types, IConventionEntityType type)
    {
        if (!types.Contains(type))
            types.Add(type);
    }

    // A required key between two types of the aggregate that does not cascade makes EF Core reject Remove(root)
    // at once (Immediate timing: the relationship is severed), before the save can turn the delete into a soft
    // delete. Every such key counts, whether or not a navigation of the boundary walk reaches it. ClientNoAction
    // leaves the child untouched, so the save marks it; ownership keys follow their owner. A key counts through
    // inheritance too: one a member inherits, or whose principal is a mapped base type of a member (the root's
    // base holding its collection).
    private static void EnsureCascades(IReadOnlyEntityType root, HashSet<IReadOnlyEntityType> members)
    {
        foreach (var type in members)
        {
            foreach (var foreignKey in type.GetForeignKeys())
            {
                if (foreignKey.IsOwnership
                    || !foreignKey.PrincipalEntityType.GetDerivedTypesInclusive().Any(members.Contains)
                    || !foreignKey.IsRequired
                    || foreignKey.DeleteBehavior is not (DeleteBehavior.Restrict or DeleteBehavior.NoAction
                        or DeleteBehavior.ClientSetNull or DeleteBehavior.SetNull))
                {
                    continue;
                }

                var key = $"{foreignKey.DeclaringEntityType.ClrType.Name}.{string.Join(", ", foreignKey.Properties.Select(p => p.Name))}";
                var navigation = (IReadOnlyNavigationBase?)foreignKey.PrincipalToDependent ?? foreignKey.DependentToPrincipal;
                var through = navigation is null
                    ? "no navigation"
                    : $"through '{navigation.DeclaringEntityType.ClrType.Name}.{navigation.Name}'";
                throw new InvalidOperationException(
                    $"The aggregate '{root.ClrType.Name}' is configured with HasSoftDelete(), but its required foreign key '{key}' " +
                    $"({through}) is configured with OnDelete(DeleteBehavior.{foreignKey.DeleteBehavior}): under EF Core's Immediate " +
                    "cascade timing, removing its principal throws at once, before the save can turn a delete into a soft delete. " +
                    "Use DeleteBehavior.Cascade inside the aggregate (or ClientCascade, where the database refuses several cascade paths).");
            }
        }
    }

    private static void Mark(
        IConventionModel model,
        IConventionEntityType root,
        IConventionEntityType hierarchy,
        IReadOnlyList<IConventionEntityType> aggregate)
    {
        foreach (var type in hierarchy.GetDerivedTypesInclusive())
        {
            if (type.FindDeclaredProperty(SoftDelete.PropertyName) is { } declared && !declared.IsShadowProperty())
                throw Reserved(root, type, "maps it to a CLR member");
        }

        if (hierarchy.GetDeclaredQueryFilters().Any(f => f.IsAnonymous))
        {
            throw new InvalidOperationException(
                $"'{hierarchy.DisplayName()}' belongs to the soft-deletable aggregate '{root.ClrType.Name}' and carries an anonymous " +
                $"query filter, which EF Core cannot combine with the named filter '{SoftDelete.FilterName}'. Name the other " +
                "filter: HasQueryFilter(\"Tenant\", e => ...).");
        }

        hierarchy.Builder.Property(typeof(DateTimeOffset?), SoftDelete.PropertyName);
        var mark = hierarchy.FindProperty(SoftDelete.PropertyName);
        if (mark is null)
            throw Reserved(root, hierarchy, "ignores it");
        if (mark.ClrType != typeof(DateTimeOffset?))
            throw Reserved(root, hierarchy, $"configures it as '{mark.ClrType.Name}', not 'DateTimeOffset?'");
        EnsureOnlyTheColumnIsConfigured(root, hierarchy, mark);

        EnsureOwnColumn(model, root, hierarchy, mark, aggregate);

        var entity = Expression.Parameter(hierarchy.ClrType, "e");
        var filter = Expression.Lambda(
            Expression.Equal(
                Expression.Call(s_property, entity, Expression.Constant(SoftDelete.PropertyName)),
                Expression.Constant(null, typeof(DateTimeOffset?))),
            entity);
        hierarchy.Builder.HasQueryFilter(SoftDelete.FilterName, filter);
    }

    // A null mark means live: a required mark, a default, a generated value or a converter would make new
    // rows deleted, fail every insert, or change what the filter compares.
    private static void EnsureOnlyTheColumnIsConfigured(IReadOnlyEntityType root, IReadOnlyEntityType hierarchy, IConventionProperty mark)
    {
        if (!mark.IsNullable)
            throw Reserved(root, hierarchy, "configures it as required");
        if (mark.GetDefaultValueSql() is not null || mark.TryGetDefaultValue(out _) || mark.GetComputedColumnSql() is not null)
            throw Reserved(root, hierarchy, "gives it a default value, or a computed one");
        if (mark.ValueGenerated != ValueGenerated.Never)
            throw Reserved(root, hierarchy, "configures it as generated");
        if (mark.GetValueConverter() is not null || mark.GetProviderClrType() is not null)
            throw Reserved(root, hierarchy, "gives it a value converter");
    }

    // In every table that stores the mark: the hierarchy's, or each concrete type's under TPC (an abstract TPC
    // base has none).
    private static void EnsureOwnColumn(
        IConventionModel model,
        IReadOnlyEntityType root,
        IConventionEntityType hierarchy,
        IConventionProperty mark,
        IReadOnlyList<IConventionEntityType> aggregate)
    {
        foreach (var table in TableColumns.TablesOf(hierarchy))
        {
            if (mark.GetColumnName(table) is not { } column)
                continue;

            foreach (var (property, other) in TableColumns.PropertiesIn(model, table))
            {
                // The mark of another type of the same aggregate in the same table (table splitting) is the
                // same column, by design.
                if (property.Name == SoftDelete.PropertyName && aggregate.Any(t => t.IsAssignableFrom(property.DeclaringType)))
                    continue;

                if (!ReferenceEquals(property, mark) && string.Equals(other, column, StringComparison.OrdinalIgnoreCase))
                {
                    throw Reserved(root, hierarchy,
                        $"its column '{column}' in '{table.DisplayName()}' is also mapped by '{property.DeclaringType.ClrType.Name}.{property.Name}'");
                }
            }
        }
    }

    private static InvalidOperationException Reserved(IReadOnlyEntityType root, IReadOnlyEntityType type, string reason) =>
        new($"'{SoftDelete.PropertyName}' is reserved on '{type.ClrType.Name}', an entity of the soft-deletable aggregate " +
            $"'{root.ClrType.Name}', for its deletion mark, but '{type.ClrType.Name}' {reason}. Rename that property or column; " +
            "the mark itself may only have its column configured.");
}
