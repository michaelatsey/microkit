namespace MicroKit.Result;

/// <summary>
/// Guard methods for parameter validation across Result methods.
/// </summary>
internal static class ResultGuard
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void NotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
            ThrowHelper.ThrowArgumentNull(paramName!);
    }
}
