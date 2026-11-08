namespace MicroKit.Result;

/// <summary>
/// Provides factory methods for creating error metadata dictionaries.
/// </summary>
public static class ErrorMetadata
{
    /// <summary>Gets an empty metadata dictionary.</summary>
    public static IReadOnlyDictionary<string, object?> Empty { get; }
        = ImmutableDictionary<string, object?>.Empty;

    /// <summary>
    /// Creates metadata from a single key-value pair.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>An immutable dictionary containing the specified pair.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, object?> Create(string key, object? value) =>
        ImmutableDictionary<string, object?>.Empty.SetItem(key, value); // immutable: an error never changes, even cast

    /// <summary>
    /// Creates metadata from multiple key-value pairs.
    /// </summary>
    /// <param name="pairs">The key-value pairs to include.</param>
    /// <returns>An immutable dictionary containing all specified pairs; for a repeated key, the last value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a key is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, object?> Create(
        params ReadOnlySpan<KeyValuePair<string, object?>> pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object?>();
        foreach (var pair in pairs)
            builder[pair.Key] = pair.Value;
        return builder.ToImmutable();
    }
}
