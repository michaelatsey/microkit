namespace MicroKit.Messaging.Processing;

/// <summary>Cuts an error text to its column.</summary>
internal static class ErrorText
{
    /// <summary>
    /// <paramref name="value"/>, at most <paramref name="max"/> characters long, never ending on half of a
    /// surrogate pair: that is not valid text, and a strict encoder — Npgsql's — rejects the write, which
    /// fails every outcome settled with it.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="max">The column's length, at least 1.</param>
    /// <returns>The text, cut if it is longer than <paramref name="max"/>.</returns>
    public static string? Cut(string? value, int max)
    {
        if (value is null || value.Length <= max)
        {
            return value;
        }

        return value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];
    }
}
