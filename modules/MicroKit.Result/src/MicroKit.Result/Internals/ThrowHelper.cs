namespace MicroKit.Result;

/// <summary>
/// Internal throw helpers to keep hot paths small and inlineable by the JIT.
/// </summary>
internal static class ThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentNull(string paramName) =>
        throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowResultFailure(IError error) =>
        throw new ResultException(
            $"Cannot access Value on a failed result. Error: [{error.Code}] {error.Message}",
            [error]);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowResultSuccess() =>
        throw new ResultException(
            "Cannot access Error on a successful result.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowResultUninitialized() =>
        throw new ResultException(
            "Result was not initialized. Use Result.Success() or Result.Failure() factory methods.");
}
