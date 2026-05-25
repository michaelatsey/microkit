namespace MicroKit.Logging.Generators;

/// <summary>Equatable data model produced by the incremental pipeline for a single decorated type.</summary>
internal sealed record LogScopeModel(
    string TypeName,
    string Namespace,
    bool IsRecord,
    EquatableArray<LogPropertyModel> Properties);

/// <summary>Represents a single readable instance property on the decorated type.</summary>
internal sealed record LogPropertyModel(string Name, bool IsNullable);

/// <summary>
/// Readonly struct wrapper around <see cref="ImmutableArray{T}"/> with structural equality.
/// Required so that <see cref="LogScopeModel"/> participates correctly in Roslyn's incremental
/// caching — the generator is only re-executed when the model actually changes.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public ImmutableArray<T> AsImmutableArray() => _array;

    public bool Equals(EquatableArray<T> other)
    {
        var a = _array.IsDefault ? ImmutableArray<T>.Empty : _array;
        var b = other._array.IsDefault ? ImmutableArray<T>.Empty : other._array;

        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var arr = _array.IsDefault ? ImmutableArray<T>.Empty : _array;
        int hash = 17;
        foreach (var item in arr)
            hash = hash * 31 + item.GetHashCode();
        return hash;
    }
}

