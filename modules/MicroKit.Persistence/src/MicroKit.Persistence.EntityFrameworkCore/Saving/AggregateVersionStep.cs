namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The save step that bumps the version (<see cref="AggregateVersion.PropertyName"/>) of every
/// aggregate root whose boundary has an <c>Added</c>, <c>Modified</c> or <c>Deleted</c> entity, the
/// root itself included. Always the last step of <see cref="AggregateSaveInterceptor"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>An entity outside every boundary (an outbox row, reference data) is
///     ignored.</description></item>
///   <item><description>A child is attached to its root through the boundary
///     (<see cref="AggregateBoundaryMap"/>): by its original foreign key when it is deleted or moves
///     out, by its current foreign key otherwise; a child the parent references matches the
///     parent's original or current foreign key.</description></item>
///   <item><description>A child that moves from one aggregate to another bumps both roots, and both
///     must have been loaded whole or staged by a repository (<see cref="AggregateLoadRecord"/>);
///     otherwise the save fails, naming the roots.</description></item>
///   <item><description>A changed child whose root is not tracked fails the save: its version
///     could not be bumped, and skipping it would defeat the check. That includes a child moving to
///     or from an aggregate whose root is not tracked: a foreign key that points to a parent the
///     context does not track leaves that side unresolved.</description></item>
///   <item><description>The bump: an <c>Added</c> root gets 1; any other gets its original value
///     plus one — computed from the original, so a repeated save never adds twice; a
///     <c>Deleted</c> root keeps its original value, which its <c>DELETE</c> checks.</description></item>
/// </list>
/// </remarks>
internal static class AggregateVersionStep
{
    /// <summary>Bumps the versions and returns what it changed, so a failed save can undo it.</summary>
    internal static IReadOnlyList<VersionBump> Apply(AggregateSave save)
    {
        var roots = new Dictionary<object, EntityEntry>(ReferenceEqualityComparer.Instance);

        foreach (var entry in save.Changed)
        {
            if (AggregateBoundaryMap.IsRoot(entry.Metadata))
            {
                roots.TryAdd(entry.Entity, entry);
                continue;
            }

            if (!save.Map.IsInside(entry.Metadata))
                continue; // outside every boundary

            var current = entry.State == EntityState.Deleted ? ResolvedRoots.None : save.RootsOf(entry, original: false);
            var original = entry.State == EntityState.Added || (entry.State == EntityState.Modified && !LinkChanged(save, entry))
                ? ResolvedRoots.None
                : save.RootsOf(entry, original: true);

            if (current.Unresolved || original.Unresolved || (current.Roots.Count == 0 && original.Roots.Count == 0))
                throw RootNotTracked(save, entry);

            if (current.Roots.Count > 0 && original.Roots.Count > 0 && !SameRoots(current.Roots, original.Roots))
                EnsureMoveIsRecorded(save, entry, original.Roots, current.Roots);

            foreach (var root in current.Roots.Concat(original.Roots))
                roots.TryAdd(root.Entity, root);
        }

        var bumps = new List<VersionBump>(roots.Count);
        foreach (var root in roots.Values)
        {
            if (Bump(root) is { } bump)
                bumps.Add(bump);
        }

        return bumps;
    }

    /// <summary>Puts back the versions a failed save had bumped.</summary>
    internal static void Undo(IReadOnlyList<VersionBump> bumps)
    {
        foreach (var bump in bumps)
        {
            if (bump.Root.State is EntityState.Detached or EntityState.Deleted)
                continue;

            var version = bump.Root.Property(AggregateVersion.PropertyName);
            if (bump.Root.State != EntityState.Added && Equals(bump.Previous, version.OriginalValue))
                version.IsModified = false; // back to the loaded value, and to Unchanged when nothing else changed
            else
                version.CurrentValue = bump.Previous;
        }
    }

    // Whether a foreign key the entry holds towards a parent changed in this save.
    private static bool LinkChanged(AggregateSave save, EntityEntry entry)
    {
        foreach (var link in save.Map.LinksOf(entry.Metadata))
        {
            if (!link.ChildIsDependent || !link.ForeignKey.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
                continue;
            if (!save.ComparerFor(link.ForeignKey).Equals(
                    AggregateSave.ValuesOf(entry, link.ForeignKey.Properties, original: true),
                    AggregateSave.ValuesOf(entry, link.ForeignKey.Properties, original: false)))
                return true;
        }

        return false;
    }

    private static bool SameRoots(IReadOnlyList<EntityEntry> left, IReadOnlyList<EntityEntry> right) =>
        left.Count == right.Count && left.All(l => right.Any(r => ReferenceEquals(l.Entity, r.Entity)));

    private static void EnsureMoveIsRecorded(
        AggregateSave save,
        EntityEntry child,
        IReadOnlyList<EntityEntry> from,
        IReadOnlyList<EntityEntry> to)
    {
        var unrecorded = from.Concat(to)
            .Where(root => save.Record?.IsRecorded(root.Entity) != true)
            .ToList();
        if (unrecorded.Count == 0)
            return;

        throw new InvalidOperationException(
            $"'{child.Metadata.ClrType.Name}' moves from {Describe(from)} to {Describe(to)}: a move bumps both aggregates and " +
            $"requires both to be loaded whole or staged by their repository, but {Describe(unrecorded)} was not. Load " +
            "both aggregates through their repositories before moving the entity.");
    }

    private static InvalidOperationException RootNotTracked(AggregateSave save, EntityEntry child) =>
        new($"'{child.Metadata.ClrType.Name}' changed, but the root of its aggregate, " +
            $"'{save.Map.RootOf(child.Metadata).ClrType.Name}', is not tracked: its version cannot be bumped. Load the " +
            "aggregate through its repository before changing it.");

    private static string Describe(IReadOnlyList<EntityEntry> roots) =>
        string.Join(" and ", roots.Select(r =>
            $"'{r.Metadata.ClrType.Name}' ({AggregateSave.ValuesOf(r, r.Metadata.FindPrimaryKey()!.Properties, original: false)})"));

    private static VersionBump? Bump(EntityEntry root)
    {
        if (root.State == EntityState.Deleted || root.Metadata.FindProperty(AggregateVersion.PropertyName) is not { } token)
            return null;

        var version = root.Property(token.Name);
        var previous = version.CurrentValue;
        version.CurrentValue = root.State == EntityState.Added
            ? 1L
            : (long)version.OriginalValue! + 1L;
        return new VersionBump(root, previous);
    }

}

/// <summary>A version the step changed: the root and the value it held before.</summary>
internal readonly record struct VersionBump(EntityEntry Root, object? Previous);
