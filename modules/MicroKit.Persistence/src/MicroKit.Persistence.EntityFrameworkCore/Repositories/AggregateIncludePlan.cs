using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The navigations that make up a whole aggregate, read once per entity type from the EF Core
/// model: the include paths <c>EfRepository.FindAsync</c> applies, and whether the load is split.
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="bullet">
///   <item><description>Every navigation — reference, collection, or skip (many-to-many) — whose
///     target type does not implement <see cref="IAggregateRoot"/> is followed, recursively:
///     another aggregate root is outside the boundary and is never loaded.</description></item>
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
/// Consequences of the rules, overridable through <c>EfRepository.IncludeAggregate</c>: an entity
/// that is not marked <see cref="IAggregateRoot"/> is inside the boundary, reference data included
/// (with its own collections); a self-referencing child (a tree) is not followed below its first
/// level; and mutually linked child types produce one include path per distinct route.
/// </para>
/// </remarks>
internal sealed class AggregateIncludePlan
{
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

    private static AggregateIncludePlan Build(IEntityType root)
    {
        var paths = new List<string>();
        var collections = 0;
        var ancestors = new HashSet<Type> { root.ClrType };
        Walk(root, prefix: string.Empty, ancestors, paths, ref collections);
        return new AggregateIncludePlan(paths, collections);
    }

    private static void Walk(
        IEntityType type,
        string prefix,
        HashSet<Type> ancestors,
        List<string> paths,
        ref int collections)
    {
        foreach (var (name, target, isCollection) in NavigationsOf(type))
        {
            if (typeof(IAggregateRoot).IsAssignableFrom(target.ClrType))
                continue; // another aggregate: outside the boundary

            // A cycle on this path (e.g. a child's navigation back to its parent). Compared on the CLR
            // type: each ownership of an owned type is a distinct IEntityType.
            if (ancestors.Contains(target.ClrType))
                continue;

            var path = prefix + name;
            if (!target.IsOwned())
                paths.Add(path);

            // An owned collection mapped to its own table is a join exactly like a regular one, so
            // it counts towards the split; one mapped to JSON is a column and does not.
            if (isCollection && !(target.IsOwned() && target.IsMappedToJson()))
                collections++;

            ancestors.Add(target.ClrType);
            Walk(target, path + ".", ancestors, paths, ref collections);
            ancestors.Remove(target.ClrType);
        }
    }

    // The navigations of a type (inherited ones included) and those declared on its derived types,
    // each listed once. A string include names a navigation without saying on which derived type it
    // is declared, so two derived types declaring the same name with different targets cannot be
    // expressed: that fails here, at the first load, rather than loading the wrong one.
    private static List<(string Name, IEntityType Target, bool IsCollection)> NavigationsOf(IEntityType type)
    {
        var found = new Dictionary<string, (string Name, IEntityType Target, bool IsCollection)>(StringComparer.Ordinal);

        void Add(string name, IEntityType target, bool isCollection)
        {
            if (found.TryGetValue(name, out var existing))
            {
                if (existing.Target.ClrType != target.ClrType)
                {
                    throw new InvalidOperationException(
                        $"The aggregate load of '{type.ClrType.Name}' cannot include '{name}': derived types declare " +
                        $"navigations of that name to '{existing.Target.ClrType.Name}' and '{target.ClrType.Name}'. " +
                        "Override EfRepository.IncludeAggregate to state the includes explicitly.");
                }

                return;
            }

            found[name] = (name, target, isCollection);
        }

        foreach (var navigation in type.GetNavigations())
            Add(navigation.Name, navigation.TargetEntityType, navigation.IsCollection);
        foreach (var skip in type.GetSkipNavigations())
            Add(skip.Name, skip.TargetEntityType, skip.IsCollection);

        foreach (var derived in type.GetDerivedTypes())
        {
            foreach (var navigation in derived.GetDeclaredNavigations())
                Add(navigation.Name, navigation.TargetEntityType, navigation.IsCollection);
            foreach (var skip in derived.GetDeclaredSkipNavigations())
                Add(skip.Name, skip.TargetEntityType, skip.IsCollection);
        }

        return [.. found.Values];
    }
}
