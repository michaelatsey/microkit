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

    // A pattern, not Enum.IsDefined: no enum metadata is read.
    internal static Serialization.ResultJsonView DefinedView(
        Serialization.ResultJsonView view,
        [CallerArgumentExpression(nameof(view))] string? paramName = null) =>
        view is Serialization.ResultJsonView.Client or Serialization.ResultJsonView.Full
            ? view
            : throw new ArgumentOutOfRangeException(paramName, view, "Expected ResultJsonView.Client or ResultJsonView.Full.");
}
