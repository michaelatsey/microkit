using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The navigations that make up a whole aggregate, read once per entity type from the EF Core
/// model: the include paths <c>EfRepository</c> applies, and whether the load is split.
/// </summary>
/// <remarks>
/// <para>Rules (<see cref="Walk"/>, shared with <see cref="AggregateBoundaryConvention"/>):</para>
/// <list type="bullet">
///   <item><description>Every navigation — reference, collection, or skip (many-to-many) — whose
///     target type does not implement <see cref="IAggregateRoot"/> is followed, recursively:
///     another aggregate root is outside the boundary and is never loaded.</description></item>
///   <item><description>A navigation marked <c>IsOutsideAggregate()</c> is not followed: its target
///     (reference data) is outside the boundary.</description></item>
///   <item><description>Navigations declared on derived types of the root or of a child are
///     followed too: a string include names a navigation of a derived type.</description></item>
///   <item><description>A cycle is detected only against the ancestors on the current path, never
///     against every type reached so far: two distinct navigations to the same child type are both
///     loaded.</description></item>
///   <item><description>Owned navigations are not listed (EF Core includes them automatically) but
///     their own navigations are followed.</description></item>
///   <item><description>With two or more collection navigations — owned collections mapped to a table
///     included — the load is split (<c>AsSplitQuery</c>) to avoid a cartesian explosion.</description></item>
/// </list>
/// <para>
/// A self-referencing child (a tree) is not followed below its first level, and mutually linked
/// child types produce one include path per distinct route.
/// </para>
/// <para>
/// The plan is computed from the runtime model and cached against its entity type; the model
/// itself carries no plan annotation, so a compiled model needs nothing generated for it.
/// </para>
/// </remarks>
internal sealed class AggregateIncludePlan
{
    /// <summary>The model annotation <c>IsOutsideAggregate()</c> sets on a navigation.</summary>
    internal const string OutsideAggregateAnnotation = "MicroKit:OutsideAggregate";

    // Keyed on the model's entity type: one plan per model, collected with the model.
    private static readonly ConditionalWeakTable<IEntityType, AggregateIncludePlan> s_plans = new();

    private AggregateIncludePlan(IReadOnlyList<string> paths, int collectionCount)
    {
        Paths = paths;
        CollectionCount = collectionCount;
    }

    /// <summary>Gets the dotted include paths, root excluded.</summary>
    internal IReadOnlyList<string> Paths { get; }

    /// <summary>Gets the number of collection navigations across all paths.</summary>
    internal int CollectionCount { get; }

    /// <summary>Gets a value indicating whether the load runs as a split query.</summary>
    internal bool IsSplit => CollectionCount >= 2;

    internal static AggregateIncludePlan For(IEntityType root) =>
        s_plans.GetValue(root, static r => Build(r));

    /// <summary>
    /// Visits every step of <paramref name="root"/>'s boundary: the dotted path from the root, the
    /// target entity type, whether the navigation is a collection, and the navigations — one, or one
    /// per derived type declaring a navigation of that name — the step follows.
    /// </summary>
    internal static void Walk(IReadOnlyEntityType root, Action<AggregateStep> visit)
    {
        var ancestors = new HashSet<Type> { root.ClrType };
        WalkFrom(root, prefix: string.Empty, ancestors, visit);
    }

    private static AggregateIncludePlan Build(IEntityType root)
    {
        var paths = new List<string>();
        var collections = 0;
        Walk(root, step =>
        {
            if (!step.Target.IsOwned())
                paths.Add(step.Path);

            // An owned collection mapped to its own table is a join exactly like a regular one, so
            // it counts towards the split; one mapped to JSON is a column and does not.
            if (step.IsCollection && !(step.Target.IsOwned() && step.Target.IsMappedToJson()))
                collections++;
        });
        return new AggregateIncludePlan(paths, collections);
    }

    private static void WalkFrom(
        IReadOnlyEntityType type,
        string prefix,
        HashSet<Type> ancestors,
        Action<AggregateStep> visit)
    {
        foreach (var (name, target, isCollection, navigations) in NavigationsOf(type))
        {
            if (typeof(IAggregateRoot).IsAssignableFrom(target.ClrType))
                continue; // another aggregate: outside the boundary

            // A cycle on this path (e.g. a child's navigation back to its parent). Compared on the CLR
            // type: each ownership of an owned type is a distinct entity type.
            if (ancestors.Contains(target.ClrType))
                continue;

            var path = prefix + name;
            visit(new AggregateStep(path, target, isCollection, navigations));

            ancestors.Add(target.ClrType);
            WalkFrom(target, path + ".", ancestors, visit);
            ancestors.Remove(target.ClrType);
        }
    }

    // The navigations of a type (inherited ones included) and those declared on its derived types,
    // each listed once, minus those marked outside the aggregate. A string include names a
    // navigation without saying on which derived type it is declared, so two derived types
    // declaring the same name with different targets cannot be expressed: that fails here, when the
    // model is built.
    private static List<(string Name, IReadOnlyEntityType Target, bool IsCollection, List<IReadOnlyNavigationBase> Navigations)> NavigationsOf(
        IReadOnlyEntityType type)
    {
        var found = new Dictionary<string, (string Name, IReadOnlyEntityType Target, bool IsCollection, List<IReadOnlyNavigationBase> Navigations)>(
            StringComparer.Ordinal);

        void Add(IReadOnlyNavigationBase navigation)
        {
            if (navigation.FindAnnotation(OutsideAggregateAnnotation)?.Value is true)
                return;

            var name = navigation.Name;
            var target = navigation.TargetEntityType;
            if (found.TryGetValue(name, out var existing))
            {
                if (existing.Target.ClrType != target.ClrType)
                {
                    throw new InvalidOperationException(
                        $"The aggregate boundary of '{type.ClrType.Name}' cannot include '{name}': derived types declare " +
                        $"navigations of that name to '{existing.Target.ClrType.Name}' and '{target.ClrType.Name}'. " +
                        "Rename one of them.");
                }

                // The same name to the same type on two derived types: one include, two foreign keys.
                if (!existing.Navigations.Contains(navigation))
                    existing.Navigations.Add(navigation);
                return;
            }

            found[name] = (name, target, navigation.IsCollection, [navigation]);
        }

        foreach (var navigation in type.GetNavigations())
            Add(navigation);
        foreach (var skip in type.GetSkipNavigations())
            Add(skip);

        foreach (var derived in type.GetDerivedTypes())
        {
            foreach (var navigation in derived.GetDeclaredNavigations())
                Add(navigation);
            foreach (var skip in derived.GetDeclaredSkipNavigations())
                Add(skip);
        }

        return [.. found.Values];
    }
}

/// <summary>One step of an aggregate boundary walk (<see cref="AggregateIncludePlan.Walk"/>).</summary>
/// <param name="Path">The dotted include path from the root.</param>
/// <param name="Target">The entity type the step reaches.</param>
/// <param name="IsCollection">Whether the navigation is a collection.</param>
/// <param name="Navigations">The navigations of that name the step follows.</param>
internal readonly record struct AggregateStep(
    string Path,
    IReadOnlyEntityType Target,
    bool IsCollection,
    IReadOnlyList<IReadOnlyNavigationBase> Navigations);
