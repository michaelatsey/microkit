using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// The soft-delete step of the save pipeline, before the version step: a deleted root of a soft-deletable
/// aggregate, and every tracked entry of its boundary, are written as an update that sets
/// <see cref="SoftDelete.PropertyName"/> instead of a delete.
/// </summary>
/// <remarks>
/// <para>
/// It runs in <c>SavingChanges</c>, before EF Core's <c>StateManager.SaveChanges</c> calls
/// <c>GetEntriesToSave(cascadeChanges: true)</c>, which handles conceptual nulls and cascades the deletes
/// still pending (EF Core 10, <c>DbContext.SaveChangesAsync</c>). A boundary child can therefore still be
/// <c>Unchanged</c> here (<c>ClientNoAction</c>; <c>CascadeDeleteTiming</c> or <c>DeleteOrphansTiming</c>
/// <c>OnSaveChanges</c>), already <c>Deleted</c> (<c>Immediate</c> cascade), or <c>Modified</c> with a
/// nulled foreign key (<c>ClientSetNull</c>), or carry a conceptual null: every tracked entry of the boundary
/// is marked, whatever its state, and none is left <c>Deleted</c> for EF Core to cascade.
/// </para>
/// <para>
/// An entry's root is resolved by its current foreign key, or by its original one when the entry is
/// <c>Deleted</c> or its foreign key was nulled; a child moved to another, live root follows that root.
/// An owned entry of a marked entity has no mark column: it is restored without one.
/// </para>
/// </remarks>
internal static class AggregateSoftDeleteStep
{
    /// <summary>Marks the soft-deleted aggregates of the save; returns the entries it marked or restored.</summary>
    internal static IReadOnlyList<EntityEntry> Apply(AggregateSave save)
    {
        var roots = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in save.Changed)
        {
            if (entry.State != EntityState.Deleted
                || !AggregateBoundaryMap.IsRoot(entry.Metadata)
                || !SoftDeleteConvention.IsSoftDeletable(TopMostRoot(entry.Metadata)))
            {
                continue;
            }

            if (save.Record?.IsRecorded(entry.Entity) != true)
            {
                throw new InvalidOperationException(
                    $"The '{entry.Metadata.ClrType.Name}' being deleted belongs to a soft-deletable aggregate, but it was not loaded " +
                    "whole by its repository: the entities of the aggregate that are not tracked could not be marked. Load it " +
                    "through its repository before deleting it.");
            }

            roots.Add(entry.Entity);
        }

        if (roots.Count == 0)
            return [];

        // Computed first, applied after: the resolution must see the states EF Core left.
        var marked = new List<EntityEntry>();
        var owned = new List<EntityEntry>();
        foreach (var entry in save.All)
        {
            if (entry.State == EntityState.Detached)
                continue;
            if (entry.Metadata.IsOwned())
            {
                owned.Add(entry);
                continue;
            }

            if (AggregateBoundaryMap.IsRoot(entry.Metadata))
            {
                if (roots.Contains(entry.Entity))
                    marked.Add(entry);
                continue;
            }

            if (!save.Map.IsInside(entry.Metadata))
                continue;

            if (RootsOf(save, entry).Any(root => roots.Contains(root.Entity)))
                marked.Add(entry);
        }

        var markedEntities = new HashSet<object>(marked.Select(e => e.Entity), ReferenceEqualityComparer.Instance);
        var restored = owned.Where(entry => OwnedByMarked(save, entry, markedEntities)).ToList();

        var mark = ClockOf(save.Context).GetUtcNow();
        foreach (var entry in marked)
        {
            RestoreForeignKeys(save, entry);
            Undelete(entry);
            entry.Property(SoftDelete.PropertyName).CurrentValue = mark;
        }

        // An owned value is restored only while its owner still holds it: one the command replaced or
        // removed stays deleted, and detecting the owner's changes again tracks its replacement, which EF
        // Core then writes over the deleted one.
        foreach (var entry in restored)
        {
            if (entry.State != EntityState.Deleted || StillOwned(save, entry))
                Undelete(entry);
        }

        foreach (var entry in marked.Concat(restored))
        {
            if (entry.State != EntityState.Detached)
                TrackReplacedOwnedValues(entry);
        }

        return [.. marked, .. restored];
    }

    private static IEntityType TopMostRoot(IEntityType type)
    {
        var root = type;
        while (root.BaseType is { } baseType && AggregateBoundaryMap.IsRoot(baseType))
            root = baseType;
        return root;
    }

    // The current keys decide: an entry belongs to the aggregate its current foreign key — or, for a
    // principal-side child, the parent's current key — names, even when it is Deleted (a child moved into
    // the deleted root and cascaded with it). Only an entry whose current keys name no tracked root (a key
    // EF Core nulled, a child let go) falls back to its original keys.
    private static IReadOnlyList<EntityEntry> RootsOf(AggregateSave save, EntityEntry entry)
    {
        var current = save.RootsOf(entry, original: false, strict: true);
        if (current.Roots.Count > 0 || entry.State == EntityState.Added)
            return current.Roots;
        return save.RootsOf(entry, original: true, strict: true).Roots;
    }

    // Puts back a foreign key EF Core nulled, or rewrites an unchanged one: writing the value clears a
    // conceptual null, which a required key keeps behind its original value. A key the save changed (a move
    // into the aggregate) is left as is.
    private static void RestoreForeignKeys(AggregateSave save, EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
            return;

        foreach (var link in DependentLinks(save, entry))
        {
            var original = AggregateSave.ValuesOf(entry, link.Properties, original: true);
            if (original.HasNull)
                continue;

            var current = AggregateSave.ValuesOf(entry, link.Properties, original: false);
            if (!current.HasNull && !save.ComparerFor(link).Equals(original, current))
                continue;

            for (var i = 0; i < link.Properties.Count; i++)
                entry.Property(link.Properties[i].Name).CurrentValue = original.Values[i];
        }
    }

    private static IEnumerable<IForeignKey> DependentLinks(AggregateSave save, EntityEntry entry) =>
        save.Map.LinksOf(entry.Metadata)
            .Where(link => link.ChildIsDependent && link.ForeignKey.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
            .Select(link => link.ForeignKey);

    // Deleted → Unchanged, then every property — complex properties included — that differs from its
    // original is flagged again, so a change made before the delete in this save is written with the mark.
    private static void Undelete(EntityEntry entry)
    {
        if (entry.State == EntityState.Deleted)
            entry.State = EntityState.Unchanged;
        if (entry.State == EntityState.Added)
            return;

        Reflag(entry.Properties);
        foreach (var complex in entry.ComplexProperties)
            Reflag(complex);
    }

    private static void Reflag(ComplexPropertyEntry complex)
    {
        Reflag(complex.Properties);
        foreach (var nested in complex.ComplexProperties)
            Reflag(nested);
    }

    private static void Reflag(IEnumerable<PropertyEntry> properties)
    {
        foreach (var property in properties)
        {
            if (property.Metadata.IsKey())
                continue;
            var comparer = property.Metadata.GetValueComparer();
            property.IsModified = !comparer.Equals(property.OriginalValue, property.CurrentValue);
        }
    }

    // Under Immediate cascade timing, Remove(root) detached the owned values the command had just added in
    // place of the old ones: they are tracked again, Added, so EF Core writes them over the deleted ones.
    private static void TrackReplacedOwnedValues(EntityEntry owner)
    {
        foreach (var reference in owner.References)
        {
            if (reference.Metadata is not INavigation { ForeignKey.IsOwnership: true } navigation
                || reference.TargetEntry is not { State: EntityState.Detached } target)
            {
                continue;
            }

            Reattach(owner, navigation.ForeignKey, target);
        }

        foreach (var collection in owner.Collections)
        {
            if (collection.Metadata is not INavigation { ForeignKey.IsOwnership: true } navigation
                || collection.CurrentValue is null)
            {
                continue;
            }

            foreach (var item in collection.CurrentValue.Cast<object>().ToList())
            {
                var tracked = collection.FindEntry(item);
                if (tracked is null || tracked.State == EntityState.Detached)
                    Reattach(owner, navigation.ForeignKey, tracked ?? owner.Context.Entry(item));
            }
        }
    }

    private static void Reattach(EntityEntry owner, IForeignKey ownership, EntityEntry target)
    {
        for (var i = 0; i < ownership.Properties.Count; i++)
            target.Property(ownership.Properties[i].Name).CurrentValue = owner.Property(ownership.PrincipalKey.Properties[i].Name).CurrentValue;
        target.State = EntityState.Added;
    }

    // Whether the owner still holds this owned entry through its ownership navigation.
    private static bool StillOwned(AggregateSave save, EntityEntry entry)
    {
        var ownership = entry.Metadata.FindOwnership()!;
        if (ownership.PrincipalToDependent is not { } navigation)
            return true;

        var values = AggregateSave.ValuesOf(entry, ownership.Properties, original: true);
        if (values.HasNull || save.FindByKey(ownership.PrincipalKey, values) is not { } owner)
            return false;

        var held = owner.Navigation(navigation.Name).CurrentValue;
        return navigation.IsCollection
            ? held is System.Collections.IEnumerable items && items.Cast<object>().Any(item => ReferenceEquals(item, entry.Entity))
            : ReferenceEquals(held, entry.Entity);
    }

    // An owned entry follows its owner chain up to a non-owned entity.
    private static bool OwnedByMarked(AggregateSave save, EntityEntry entry, HashSet<object> marked)
    {
        var current = entry;
        for (var depth = 0; depth < 32 && current.Metadata.IsOwned(); depth++)
        {
            var ownership = current.Metadata.FindOwnership()!;
            var values = AggregateSave.ValuesOf(current, ownership.Properties, original: current.State == EntityState.Deleted);
            if (values.HasNull || save.FindByKey(ownership.PrincipalKey, values) is not { } owner)
                return false;
            current = owner;
        }

        return marked.Contains(current.Entity);
    }

    // The application's TimeProvider: a pooled context's application provider is the root one, so a
    // TimeProvider registered as scoped is not resolvable there; without UseApplicationServiceProvider or
    // AddDbContext, the clock is TimeProvider.System.
    private static TimeProvider ClockOf(DbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?.GetService<TimeProvider>() ?? TimeProvider.System;
}
