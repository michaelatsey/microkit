using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The database never changes an aggregate on behalf of something outside it. A foreign key whose
/// dependent lies in an aggregate (a root, or an entity inside a boundary) and whose principal is
/// outside that aggregate gets <see cref="DeleteBehavior.Restrict"/>, instead of EF Core's
/// <see cref="DeleteBehavior.Cascade"/> default for a required relationship. Added by
/// <c>UseMicroKitPersistence()</c>, after <see cref="AggregateBoundaryConvention"/>.
/// </summary>
/// <remarks>
/// <para>
/// A key is inside one aggregate when it is the foreign key of a navigation the boundary walk
/// follows (<see cref="AggregateIncludePlan.Walk"/>): a child's link to its parent, an ownership, a
/// one-to-one. Every other key declared by a type that lies in an aggregate is a reference out of it —
/// to another root (of any type, the same one included), or to an entity outside every boundary.
/// Relationships inside one boundary keep their behaviour.
/// </para>
/// <para>
/// An explicit <see cref="DeleteBehavior.Cascade"/> or <see cref="DeleteBehavior.SetNull"/> on such a
/// key fails model building, naming it: those act in the database, so deleting the principal would
/// delete or change rows of the aggregate without bumping its version. Other explicit behaviours are
/// kept.
/// </para>
/// </remarks>
internal sealed class AggregateReferenceConvention : IModelFinalizingConvention
{
    /// <inheritdoc/>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var model = modelBuilder.Metadata;
        var aggregateOf = new Dictionary<IReadOnlyEntityType, IReadOnlyEntityType>();
        var inside = new HashSet<IReadOnlyForeignKey>();

        foreach (var root in model.GetEntityTypes().Where(AggregateBoundaryConvention.IsTopMostRoot))
        {
            foreach (var type in root.GetDerivedTypesInclusive())
                aggregateOf.TryAdd(type, root);

            AggregateIncludePlan.Walk(root, step =>
            {
                foreach (var type in step.Target.GetDerivedTypesInclusive())
                    aggregateOf.TryAdd(type, root);
                foreach (var navigation in step.Navigations.OfType<IReadOnlyNavigation>())
                    inside.Add(navigation.ForeignKey);
            });
        }

        // A key into a soft-deletable aggregate from outside it — another aggregate, or an entity outside
        // every boundary, join rows of an outside many-to-many included — is ClientNoAction: the principal is
        // never deleted, so EF Core must not null, refuse or delete a tracked dependent on Remove(root).
        var intoSoftDeletable = new HashSet<IReadOnlyForeignKey>();
        foreach (var type in model.GetEntityTypes().ToList())
        {
            aggregateOf.TryGetValue(type, out var dependentRoot);
            foreach (var foreignKey in type.GetDeclaredForeignKeys().ToList())
            {
                if (foreignKey.IsOwnership
                    || !aggregateOf.TryGetValue(foreignKey.PrincipalEntityType, out var principalRoot)
                    || ReferenceEquals(principalRoot, dependentRoot)
                    || !SoftDeleteConvention.IsSoftDeletable(principalRoot))
                {
                    continue;
                }

                intoSoftDeletable.Add(foreignKey);
                ClientNoAction(principalRoot, foreignKey);
            }
        }

        foreach (var type in model.GetEntityTypes().ToList())
        {
            if (!aggregateOf.TryGetValue(type, out var root))
                continue; // outside every aggregate: not this convention's concern

            foreach (var foreignKey in type.GetDeclaredForeignKeys().ToList())
            {
                if (!inside.Contains(foreignKey) && !intoSoftDeletable.Contains(foreignKey))
                    Restrict(root, foreignKey);
            }
        }
    }

    private static void ClientNoAction(IReadOnlyEntityType principalRoot, IConventionForeignKey foreignKey)
    {
        var source = foreignKey.GetDeleteBehaviorConfigurationSource();
        if (source is not (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation))
        {
            foreignKey.Builder.OnDelete(DeleteBehavior.ClientNoAction);
            return;
        }

        if (foreignKey.DeleteBehavior != DeleteBehavior.ClientNoAction)
        {
            var key = $"{foreignKey.DeclaringEntityType.ClrType.Name}.{string.Join(", ", foreignKey.Properties.Select(p => p.Name))}";
            throw new InvalidOperationException(
                $"The foreign key '{key}' references '{foreignKey.PrincipalEntityType.ClrType.Name}', inside the soft-deletable " +
                $"aggregate '{principalRoot.ClrType.Name}', from outside it, and is configured with " +
                $"OnDelete(DeleteBehavior.{foreignKey.DeleteBehavior}): EF Core would change or delete a tracked dependent when " +
                "the root is removed, although soft delete never deletes it. Remove that OnDelete: such a key is ClientNoAction.");
        }
    }

    private static void Restrict(IReadOnlyEntityType root, IConventionForeignKey foreignKey)
    {
        var source = foreignKey.GetDeleteBehaviorConfigurationSource();
        var isExplicit = source is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation;
        if (!isExplicit)
        {
            foreignKey.Builder.OnDelete(DeleteBehavior.Restrict);
            return;
        }

        if (foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull)
        {
            var key = $"{foreignKey.DeclaringEntityType.ClrType.Name}.{string.Join(", ", foreignKey.Properties.Select(p => p.Name))}";
            var principal = foreignKey.PrincipalEntityType.ClrType.Name;
            var effect = foreignKey.DeleteBehavior == DeleteBehavior.Cascade ? "delete" : "change";
            throw new InvalidOperationException(
                $"The foreign key '{key}' references '{principal}' from inside the aggregate '{root.ClrType.Name}', and is configured " +
                $"with OnDelete(DeleteBehavior.{foreignKey.DeleteBehavior}): deleting a '{principal}' would make the database {effect} " +
                $"rows of '{root.ClrType.Name}' aggregates without bumping their version. Remove that OnDelete: a reference between " +
                "aggregates is Restrict.");
        }
    }
}
