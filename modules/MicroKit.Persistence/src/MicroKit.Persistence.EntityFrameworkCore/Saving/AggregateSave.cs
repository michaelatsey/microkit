using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// One save, as the aggregate save steps see it: the tracked entries, the changed ones, the
/// boundary map, the loaded-root record, and lookups of tracked entries by key and by foreign key.
/// </summary>
/// <remarks>
/// Built from the public change-tracker API only. The lookups index every tracked entry once, on
/// first use; a foreign-key lookup matches an entry's original value as well as its current one,
/// so a parent that let go of a child in this save is still found from that child.
/// </remarks>
internal sealed class AggregateSave
{
    private readonly IReadOnlyList<EntityEntry> _entries;
    private readonly Dictionary<IKey, Dictionary<KeyValues, EntityEntry>> _byKey = [];
    private readonly Dictionary<IForeignKey, Dictionary<KeyValues, List<EntityEntry>>> _byForeignKey = [];

    private AggregateSave(DbContext context, IReadOnlyList<EntityEntry> entries, IReadOnlyList<EntityEntry> changed)
    {
        Context = context;
        _entries = entries;
        Changed = changed;
        Map = AggregateBoundaryMap.For(context.Model);
        Record = KeyLookup.FindLoadRecord(context);
    }

    internal DbContext Context { get; }

    /// <summary>Gets the entries that are <c>Added</c>, <c>Modified</c> or <c>Deleted</c>.</summary>
    internal IReadOnlyList<EntityEntry> Changed { get; }

    internal AggregateBoundaryMap Map { get; }

    /// <summary>Gets the loaded-root record; never <see langword="null"/> on a configured context.</summary>
    internal AggregateLoadRecord? Record { get; }

    /// <summary>
    /// Detects the changes (when automatic detection is on: <c>SavingChanges</c> runs before EF
    /// Core's own detection) and returns the save, or <see langword="null"/> when nothing changed.
    /// </summary>
    internal static AggregateSave? Begin(DbContext context)
    {
        var entries = context.ChangeTracker.Entries().ToList();
        var changed = entries
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        return changed.Count == 0 ? null : new AggregateSave(context, entries, changed);
    }

    /// <summary>The value of <paramref name="property"/>: the original one when asked and it exists.</summary>
    internal static object? ValueOf(EntityEntry entry, IProperty property, bool original) =>
        original && entry.State != EntityState.Added
            ? entry.OriginalValues[property]
            : entry.CurrentValues[property];

    internal static KeyValues ValuesOf(EntityEntry entry, IReadOnlyList<IProperty> properties, bool original)
    {
        var values = new object?[properties.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = ValueOf(entry, properties[i], original);
        return new KeyValues(values);
    }

    /// <summary>The tracked entry whose <paramref name="key"/> has <paramref name="values"/>.</summary>
    internal EntityEntry? FindByKey(IKey key, KeyValues values)
    {
        if (!_byKey.TryGetValue(key, out var index))
        {
            _byKey[key] = index = [];
            foreach (var entry in _entries)
            {
                if (key.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
                    index.TryAdd(ValuesOf(entry, key.Properties, original: false), entry);
            }
        }

        return index.GetValueOrDefault(values);
    }

    /// <summary>
    /// The tracked entries whose <paramref name="foreignKey"/>, current or original, has
    /// <paramref name="values"/>.
    /// </summary>
    internal IReadOnlyList<EntityEntry> FindByForeignKey(IForeignKey foreignKey, KeyValues values)
    {
        if (!_byForeignKey.TryGetValue(foreignKey, out var index))
        {
            _byForeignKey[foreignKey] = index = [];
            foreach (var entry in _entries)
            {
                if (!foreignKey.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
                    continue;

                var current = ValuesOf(entry, foreignKey.Properties, original: false);
                var original = ValuesOf(entry, foreignKey.Properties, original: true);
                AddTo(index, current, entry);
                if (!original.Equals(current))
                    AddTo(index, original, entry);
            }
        }

        return index.TryGetValue(values, out var found) ? found : [];
    }

    private static void AddTo(Dictionary<KeyValues, List<EntityEntry>> index, KeyValues values, EntityEntry entry)
    {
        if (values.HasNull)
            return;
        if (!index.TryGetValue(values, out var list))
            index[values] = list = [];
        list.Add(entry);
    }
}

/// <summary>The values of a key or foreign key, compared element by element.</summary>
internal readonly struct KeyValues(object?[] values) : IEquatable<KeyValues>
{
    private readonly object?[] _values = values;

    internal bool HasNull => Array.Exists(_values, v => v is null);

    public bool Equals(KeyValues other)
    {
        if (_values.Length != other._values.Length)
            return false;
        for (var i = 0; i < _values.Length; i++)
        {
            if (!Equals(_values[i], other._values[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is KeyValues other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _values)
            hash.Add(value);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(", ", _values);
}
