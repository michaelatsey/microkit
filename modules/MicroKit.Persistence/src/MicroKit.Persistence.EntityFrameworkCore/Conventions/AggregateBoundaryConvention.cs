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
        Dictionary<IReadOnlyEntityType, List<IReadOnlyEntityType>> owners) =>
        AggregateIncludePlan.Walk(root, (_, target, _) =>
        {
            if (target.IsOwned())
                return;

            foreach (var type in target.GetDerivedTypesInclusive())
            {
                if (!owners.TryGetValue(type, out var roots))
                    owners[type] = roots = [];
                if (!roots.Contains(root))
                    roots.Add(root);
            }
        });

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
    private static bool IsTopMostRoot(IReadOnlyEntityType type) =>
        !type.IsOwned()
        && typeof(IAggregateRoot).IsAssignableFrom(type.ClrType)
        && (type.BaseType is null || !typeof(IAggregateRoot).IsAssignableFrom(type.BaseType.ClrType));
}
