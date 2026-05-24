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
    /// <returns>A read-only dictionary containing the specified pair.</returns>
    public static IReadOnlyDictionary<string, object?> Create(string key, object? value) =>
        new Dictionary<string, object?>(1) { [key] = value };

    /// <summary>
    /// Creates metadata from multiple key-value pairs.
    /// </summary>
    /// <param name="pairs">The key-value pairs to include.</param>
    /// <returns>A read-only dictionary containing all specified pairs.</returns>
    public static IReadOnlyDictionary<string, object?> Create(
        params ReadOnlySpan<KeyValuePair<string, object?>> pairs)
    {
        var dict = new Dictionary<string, object?>(pairs.Length);
        foreach (var pair in pairs)
            dict[pair.Key] = pair.Value;
        return dict;
    }
}
