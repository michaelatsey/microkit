using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Validates, when the model is finalised, that every entity inside an aggregate boundary belongs
/// to exactly one aggregate root. Added by <c>UseMicroKitPersistence()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each root's boundary is walked as <see cref="AggregateIncludePlan.Walk"/> does. A non-root,
/// non-owned entity type reached from two roots — directly, or because one root reaches a base
/// type and the other one of its derived types — fails model building, naming the type and the
/// roots. Each ownership of an owned type is its own entity type, so the same owned CLR type under
/// two roots is not shared. A root's derived root types are part of its own hierarchy and count as
/// that root.
/// </para>
/// <para>
/// Uniqueness also holds per row: a navigation brings a non-root entity into the boundary only if
/// each of its rows can belong to one parent row — a collection or a reference whose child holds the
/// foreign key, or a reference whose parent holds a unique foreign key (one-to-one). A many-to-one
/// reference held by the parent that reaches a non-root entity fails model building, naming the
/// navigation and the fix: one row could otherwise sit inside several aggregate instances, and a
/// change to it would bump only the roots that happen to be tracked.
/// </para>
/// <para>
/// A skip navigation (many-to-many) declared by a type inside a boundary fails model building
/// whatever its target, unless it is marked <c>IsOutsideAggregate()</c>: its join rows would be neither
/// loaded nor versioned with the aggregate. The fix is an explicit link entity, a child of the root,
/// referencing the other root.
/// </para>
/// <para>
/// It validates only: it writes no annotation, so a compiled model has nothing to generate for it.
/// </para>
/// </remarks>
internal sealed class AggregateBoundaryConvention : IModelFinalizingConvention
{
    /// <inheritdoc/>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        EnsureNoOwnedNavigationIsOutside(modelBuilder.Metadata);
        var owners = new Dictionary<IReadOnlyEntityType, List<IReadOnlyEntityType>>();

        foreach (var root in modelBuilder.Metadata.GetEntityTypes().Where(IsTopMostRoot))
        {
            Walk(root, owners);
        }

        foreach (var (child, roots) in owners)
        {
            if (roots.Count < 2)
                continue;

            var names = string.Join(", ", roots.Select(r => $"'{r.ClrType.Name}'"));
            throw new InvalidOperationException(
                $"'{child.ClrType.Name}' lies inside the aggregate boundary of more than one aggregate root: {names}. " +
                "An entity belongs to exactly one aggregate. Make it an aggregate root, or mark the navigations " +
                "that reach it from all roots but one IsOutsideAggregate() (reference data).");
        }
    }

    private static void Walk(
        IReadOnlyEntityType root,
        Dictionary<IReadOnlyEntityType, List<IReadOnlyEntityType>> owners)
    {
        var boundary = new List<IReadOnlyEntityType>(root.GetDerivedTypesInclusive());
        AggregateIncludePlan.Walk(root, step =>
        {
            foreach (var navigation in step.Navigations)
                EnsureOneParentRow(root, navigation);

            boundary.AddRange(step.Target.GetDerivedTypesInclusive());
            if (step.Target.IsOwned())
                return;

            foreach (var type in step.Target.GetDerivedTypesInclusive())
            {
                if (!owners.TryGetValue(type, out var roots))
                    owners[type] = roots = [];
                if (!roots.Contains(root))
                    roots.Add(root);
            }
        });

        // A skip navigation to another root is not walked: check every type of the boundary.
        foreach (var type in boundary.Distinct())
        {
            foreach (var skip in type.GetDeclaredSkipNavigations())
            {
                if (!AggregateIncludePlan.IsOutsideAggregate(skip))
                    throw ManyToManyInside(root, skip);
            }
        }
    }

    // A row reached through this navigation must have a single parent row.
    private static void EnsureOneParentRow(IReadOnlyEntityType root, IReadOnlyNavigationBase navigation)
    {
        if (navigation is IReadOnlySkipNavigation skip)
            throw ManyToManyInside(root, skip);

        var kind = navigation switch
        {
            IReadOnlyNavigation reference when reference.IsOnDependent && !reference.ForeignKey.IsUnique =>
                $"a many-to-one reference (the foreign key on '{reference.DeclaringEntityType.ClrType.Name}' is not unique)",
            _ => null,
        };
        if (kind is null)
            return;

        var target = navigation.TargetEntityType.ClrType.Name;
        throw new InvalidOperationException(
            $"'{navigation.DeclaringEntityType.ClrType.Name}.{navigation.Name}' brings '{target}' into the aggregate boundary of " +
            $"'{root.ClrType.Name}', but it is {kind}: one '{target}' row could belong to several aggregates, and a change to " +
            $"it would bump only those that happen to be tracked. To fix it, make the target an aggregate root, or mark the " +
            "navigation IsOutsideAggregate() (reference data).");
    }

    private static InvalidOperationException ManyToManyInside(IReadOnlyEntityType root, IReadOnlySkipNavigation skip)
    {
        // Name the side the application wrote: a unidirectional many-to-many has a hidden inverse.
        if (skip.PropertyInfo is null && skip.FieldInfo is null
            && skip.Inverse is { } inverse && (inverse.PropertyInfo is not null || inverse.FieldInfo is not null))
        {
            skip = inverse;
        }

        var declaring = skip.DeclaringEntityType.ClrType.Name;
        var target = skip.TargetEntityType.ClrType.Name;
        return new InvalidOperationException(
            $"'{declaring}.{skip.Name}' is a many-to-many navigation declared inside the aggregate boundary of '{root.ClrType.Name}': " +
            $"its join rows could belong to several aggregates, and they would be neither loaded nor versioned with it. Use an " +
            $"explicit link entity, a child of the root — HasMany(x => x.{target}Links), the foreign key on the link, the link " +
            $"referencing '{target}' — or mark the navigation IsOutsideAggregate(), which leaves the links outside every aggregate.");
    }

    // An owned entity is always loaded with its owner: marking its navigation outside the aggregate
    // would only drop the owned type's own navigations from the load, leaving the aggregate partial.
    private static void EnsureNoOwnedNavigationIsOutside(IReadOnlyModel model)
    {
        foreach (var type in model.GetEntityTypes())
        {
            foreach (var navigation in type.GetDeclaredNavigations())
            {
                if (navigation.ForeignKey.IsOwnership
                    && !navigation.IsOnDependent
                    && navigation.FindAnnotation(AggregateIncludePlan.OutsideAggregateAnnotation)?.Value is true)
                {
                    throw new InvalidOperationException(
                        $"'{navigation.TargetEntityType.ClrType.Name}' is owned by '{type.ClrType.Name}' through " +
                        $"'{navigation.Name}', which is marked IsOutsideAggregate(): an owned entity is always loaded " +
                        "with its owner and belongs to its aggregate. Remove IsOutsideAggregate() from that navigation.");
                }
            }
        }
    }

    // A root whose base type is not itself a root: derived root types belong to its hierarchy.
    internal static bool IsTopMostRoot(IReadOnlyEntityType type) =>
        !type.IsOwned()
        && typeof(IAggregateRoot).IsAssignableFrom(type.ClrType)
        && (type.BaseType is null || !typeof(IAggregateRoot).IsAssignableFrom(type.BaseType.ClrType));
}
