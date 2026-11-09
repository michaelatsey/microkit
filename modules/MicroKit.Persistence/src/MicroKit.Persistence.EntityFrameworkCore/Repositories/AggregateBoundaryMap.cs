using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Which entity types lie inside an aggregate boundary, and how each one links to its parent: the
/// runtime-model view of the boundary that <see cref="AggregateVersionStep"/> resolves a changed
/// entry's root through. Built once per runtime model from the phase-1 walk
/// (<see cref="AggregateIncludePlan.Walk"/>).
/// </summary>
/// <remarks>
/// <para>A child's parent link is one of:</para>
/// <list type="bullet">
///   <item><description>dependent: the child holds the foreign key to its parent (a collection, an
///     owned type, a join entity type of a skip navigation);</description></item>
///   <item><description>principal: the parent holds the foreign key to the child (a reference such
///     as <c>Order.PrimaryContactId</c>, or the target of a skip navigation, whose "parents" are the
///     join rows that reference it).</description></item>
/// </list>
/// <para>
/// The join entity of a many-to-many navigation declared inside a boundary is inside it too, linked
/// to the declaring side, even when the navigation's target is outside (reference data, another
/// root): adding or removing a link changes the aggregate's collection.
/// </para>
/// <para>
/// An entity type that is neither a root nor reached from any root's boundary — an outbox row,
/// reference data, an <c>IsOutsideAggregate()</c> target no root reaches otherwise — is outside
/// every boundary.
/// </para>
/// </remarks>
internal sealed class AggregateBoundaryMap
{
    private static readonly ConditionalWeakTable<IModel, AggregateBoundaryMap> s_maps = new();

    private readonly Dictionary<IEntityType, List<ParentLink>> _links = [];
    private readonly Dictionary<IEntityType, IEntityType> _rootOf = [];

    internal static AggregateBoundaryMap For(IModel model) => s_maps.GetValue(model, static m => Build(m));

    /// <summary>Gets a value indicating whether <paramref name="type"/> is an aggregate root.</summary>
    internal static bool IsRoot(IEntityType type) =>
        !type.IsOwned() && typeof(IAggregateRoot).IsAssignableFrom(type.ClrType);

    /// <summary>Gets a value indicating whether <paramref name="type"/> lies inside a root's boundary.</summary>
    internal bool IsInside(IEntityType type) => _links.ContainsKey(type);

    /// <summary>The links from <paramref name="type"/> to its parents inside the boundary.</summary>
    internal IReadOnlyList<ParentLink> LinksOf(IEntityType type) =>
        _links.TryGetValue(type, out var links) ? links : [];

    /// <summary>The top-most root whose boundary holds <paramref name="type"/>.</summary>
    internal IEntityType RootOf(IEntityType type) => _rootOf[type];

    private static AggregateBoundaryMap Build(IModel model)
    {
        var map = new AggregateBoundaryMap();
        foreach (var root in model.GetEntityTypes().Where(AggregateBoundaryConvention.IsTopMostRoot))
        {
            var boundary = new List<IEntityType>(root.GetDerivedTypesInclusive());
            AggregateIncludePlan.Walk(root, step =>
            {
                boundary.AddRange(((IEntityType)step.Target).GetDerivedTypesInclusive());
                foreach (var navigation in step.Navigations)
                {
                    switch (navigation)
                    {
                        case INavigation reference:
                            // A navigation on the principal reaches a dependent; one on the dependent
                            // reaches the principal that the parent references.
                            map.Add(root, (IEntityType)step.Target, new ParentLink(reference.ForeignKey, ChildIsDependent: !reference.IsOnDependent));
                            break;

                        case ISkipNavigation skip:
                            var join = skip.JoinEntityType;
                            var toParent = skip.ForeignKey;
                            var toTarget = skip.Inverse?.ForeignKey
                                ?? join.GetForeignKeys().Single(fk => fk != toParent);
                            map.Add(root, join, new ParentLink(toParent, ChildIsDependent: true));
                            map.Add(root, (IEntityType)step.Target, new ParentLink(toTarget, ChildIsDependent: false));
                            break;
                    }
                }
            });

            // A many-to-many link from inside the boundary to reference data or to another root: the
            // target is outside, but the link row — a change to the aggregate's collection — belongs
            // to the aggregate that declares the navigation.
            foreach (var type in boundary.Distinct())
            {
                foreach (var skip in type.GetDeclaredSkipNavigations())
                {
                    if (IsRoot(skip.TargetEntityType)
                        || skip.FindAnnotation(AggregateIncludePlan.OutsideAggregateAnnotation)?.Value is true)
                    {
                        map.Add(root, skip.JoinEntityType, new ParentLink(skip.ForeignKey, ChildIsDependent: true));
                    }
                }
            }
        }

        return map;
    }

    private void Add(IEntityType root, IEntityType child, ParentLink link)
    {
        foreach (var type in child.GetDerivedTypesInclusive())
        {
            if (!_links.TryGetValue(type, out var links))
            {
                _links[type] = links = [];
                _rootOf[type] = root;
            }

            if (!links.Contains(link))
                links.Add(link);
        }
    }
}

/// <summary>A link from a child entity type to its parent inside an aggregate boundary.</summary>
/// <param name="ForeignKey">The foreign key that relates them.</param>
/// <param name="ChildIsDependent">
/// <see langword="true"/> when the child holds the foreign key; <see langword="false"/> when the
/// parent does.
/// </param>
internal readonly record struct ParentLink(IForeignKey ForeignKey, bool ChildIsDependent);
