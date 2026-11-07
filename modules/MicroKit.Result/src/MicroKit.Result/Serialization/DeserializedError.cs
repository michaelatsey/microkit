namespace MicroKit.Result.Serialization;

/// <summary>
/// An error read back from JSON by the result converters, whose original type is not known: it keeps the code,
/// message, category, severity and the metadata the payload carried.
/// </summary>
/// <remarks>Metadata values are the JSON primitives (<see cref="string"/>, <see cref="long"/>, <see cref="double"/>,
/// <see cref="bool"/>, <see langword="null"/>), or a <see cref="System.Text.Json.JsonElement"/> for an object or an array.</remarks>
/// <param name="Code">The error code.</param>
/// <param name="Message">The message.</param>
/// <param name="Category">The kind of failure.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Metadata">The metadata the payload carried; empty when it carried none.</param>
public sealed record DeserializedError(
    ErrorCode Code,
    string Message,
    ErrorCategory Category,
    ErrorSeverity Severity,
    IReadOnlyDictionary<string, object?> Metadata) : Error(Code, Message)
{
    /// <inheritdoc/>
    public override ErrorCategory Category { get; } = Category;

    /// <inheritdoc/>
    public override ErrorSeverity Severity { get; } = Severity;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata { get; } = Metadata;

    /// <summary>
    /// Determines whether two deserialized errors are equal: same code, message, category and severity, and
    /// metadata with the same entries, compared by content (a <see cref="System.Text.Json.JsonElement"/> value by its
    /// JSON), whatever the dictionary instances.
    /// </summary>
    /// <param name="other">The error to compare with.</param>
    /// <returns><see langword="true"/> if the errors are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(DeserializedError? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && base.Equals(other)
            && Category == other.Category
            && Severity == other.Severity
            && MetadataComparer.Equal(Metadata, other.Metadata));

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), Category, Severity, MetadataComparer.GetHashCode(Metadata));
}

/// <summary>
/// A validation error read back from JSON by the result converters. Like <see cref="DeserializedError"/>, a separate
/// type, it keeps <see cref="IValidationError.PropertyName"/>, so HTTP still groups it by field.
/// </summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The message.</param>
/// <param name="Category">The kind of failure.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Metadata">The metadata the payload carried; empty when it carried none.</param>
/// <param name="PropertyName">The name of the property that failed validation.</param>
public sealed record DeserializedValidationError(
    ErrorCode Code,
    string Message,
    ErrorCategory Category,
    ErrorSeverity Severity,
    IReadOnlyDictionary<string, object?> Metadata,
    string PropertyName) : Error(Code, Message), IValidationError
{
    /// <inheritdoc/>
    public override ErrorCategory Category { get; } = Category;

    /// <inheritdoc/>
    public override ErrorSeverity Severity { get; } = Severity;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> Metadata { get; } = Metadata;

    /// <summary>
    /// Determines whether two deserialized validation errors are equal: same code, message, category, severity and
    /// property name, and metadata with the same entries, compared by content as <see cref="DeserializedError"/> does.
    /// </summary>
    /// <param name="other">The error to compare with.</param>
    /// <returns><see langword="true"/> if the errors are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(DeserializedValidationError? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && base.Equals(other)
            && Category == other.Category
            && Severity == other.Severity
            && string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal)
            && MetadataComparer.Equal(Metadata, other.Metadata));

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(
            base.GetHashCode(), Category, Severity, StringComparer.Ordinal.GetHashCode(PropertyName),
            MetadataComparer.GetHashCode(Metadata));
}

// Compares metadata read back from JSON by content: the converters build a new dictionary for every read.
internal static class MetadataComparer
{
    internal static bool Equal(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        return left.Count == right.Count && Contains(left, right) && Contains(right, left); // both ways: symmetric
    }                                                                                         // whatever the comparers

    // The count only: equal metadata always has the same count, whatever the key comparers of the two dictionaries
    // (the public constructors accept any) and whatever the JsonElement values, which have no content hash.
    internal static int GetHashCode(IReadOnlyDictionary<string, object?> metadata) => metadata.Count;

    private static bool Contains(IReadOnlyDictionary<string, object?> source, IReadOnlyDictionary<string, object?> target)
    {
        foreach (var (key, value) in source)
        {
            if (!target.TryGetValue(key, out var other) || !ValueEquals(value, other))
                return false;
        }

        return true;
    }

    private static bool ValueEquals(object? left, object? right) => (left, right) switch
    {
        (System.Text.Json.JsonElement l, System.Text.Json.JsonElement r) => System.Text.Json.JsonElement.DeepEquals(l, r),
        _ => Equals(left, right),
    };
}
