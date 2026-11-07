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

    /// <summary>Returns the mapped error, or throws, keeping the caught exception, when the mapper returned null.</summary>
    internal static IError EnsureMapped(IError? mapped, Exception caught) =>
        mapped ?? throw new ArgumentNullException(
            "The exception mapper returned null; it must return an error. The caught exception is the inner exception.",
            caught);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowResultAsValue(string paramName) =>
        throw new ArgumentException(
            "A result cannot be the value of a Result<T>: a failure would become a success. Chain it with Bind instead.",
            paramName);
}
