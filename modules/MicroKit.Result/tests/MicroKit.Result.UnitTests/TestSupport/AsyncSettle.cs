namespace MicroKit.Result.UnitTests.TestSupport;

// Lets a test compile whether an operation returns a result or a pending one. Phase 1 tests that
// assert a Phase 2 fix (Tap, TapError and Finally binding an async lambda to an awaited overload)
// use it, so Phase 2 only removes their Skip.
internal static class AsyncSettle
{
    public static ValueTask<Result<T>> Settle<T>(Result<T> result) => ValueTask.FromResult(result);

    public static ValueTask<Result<T>> Settle<T>(ValueTask<Result<T>> pending) => pending;

    public static async ValueTask<Result<T>> Settle<T>(Task<Result<T>> pending) => await pending.ConfigureAwait(false);

    public static ValueTask<Result> Settle(Result result) => ValueTask.FromResult(result);

    public static ValueTask<Result> Settle(ValueTask<Result> pending) => pending;

    public static async ValueTask<Result> Settle(Task<Result> pending) => await pending.ConfigureAwait(false);
}
