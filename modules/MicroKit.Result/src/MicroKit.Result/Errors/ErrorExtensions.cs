namespace MicroKit.Result;

/// <summary>
/// Extension methods on <see cref="IError"/> for common predicates, metadata access, and conversion.
/// </summary>
public static class ErrorExtensions
{
    // ── Category / Severity predicates ───────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if the error's category matches <paramref name="category"/>.
    /// </summary>
    /// <param name="error">The error to test.</param>
    /// <param name="category">The category to compare against.</param>
    public static bool IsCategory(this IError error, ErrorCategory category)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Category == category;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the error's severity is <see cref="ErrorSeverity.Critical"/>.
    /// </summary>
    /// <param name="error">The error to test.</param>
    public static bool IsCritical(this IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Severity == ErrorSeverity.Critical;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the error's severity is <see cref="ErrorSeverity.Warning"/>.
    /// </summary>
    /// <param name="error">The error to test.</param>
    public static bool IsWarning(this IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Severity == ErrorSeverity.Warning;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the error's severity is <see cref="ErrorSeverity.Information"/>.
    /// </summary>
    /// <param name="error">The error to test.</param>
    public static bool IsInformation(this IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Severity == ErrorSeverity.Information;
    }

    // ── Metadata access ───────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if the error's metadata contains the specified key.
    /// </summary>
    /// <param name="error">The error to inspect.</param>
    /// <param name="key">The metadata key to look up.</param>
    public static bool HasMetadata(this IError error, string key)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(key);
        return error.Metadata.ContainsKey(key);
    }

    /// <summary>
    /// Retrieves a metadata value by key and casts it to <typeparamref name="T"/>.
    /// Returns <see langword="default"/> if the key is absent or the value cannot be cast.
    /// Never throws.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="error">The error to inspect.</param>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>The cast value, or <see langword="default"/> if unavailable.</returns>
    public static T? TryGetMetadata<T>(this IError error, string key)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(key);
        return error.Metadata.TryGetValue(key, out var raw) && raw is T typed
            ? typed
            : default;
    }

    // ── Conversion ────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps this error in a <see cref="ResultException"/>, useful for re-throwing at boundaries.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    /// <returns>A <see cref="ResultException"/> containing this error.</returns>
    public static ResultException ToException(this IError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ResultException(
            $"[{error.Code}] {error.Message}",
            [error]);
    }
}
