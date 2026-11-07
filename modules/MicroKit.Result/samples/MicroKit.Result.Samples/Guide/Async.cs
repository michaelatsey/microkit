namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Compose asynchronously": its C# blocks are here verbatim, modulo indentation; the rest
// only makes them build. Keep the two identical.
internal static class AsyncSamples
{
    internal static async Task AsyncDelegateSample(Result<int> result)
    {
        Result<string> mapped = await result.MapAsync(async v => await TransformAsync(v));
        Result<string> chained = await result.BindAsync(async v => await GetNextAsync(v));
        Result<int> recovered = await result.CompensateAsync(async e => await FallbackAsync(e));
        Result<int> finished = await result.FinallyAsync(async r => await LogAsync(r));

        // Tap, TapError and Finally given an async lambda await the side effect
        Result<int> audited = await result.Tap(async v => await AuditAsync(v));
    }

    internal static async Task TaskSurfaceSample()
    {
        // On Task<Result<T>> and ValueTask<Result<T>>: a synchronous delegate, applied once the task completes
        Result<string> mappedTask = await GetResultTask().Map(v => Transform(v));
        Result<string> chainedTask = await GetResultTask().Bind(v => GetNext(v));
        Result<string> mappedValueTask = await GetResultValueTask().Map(v => Transform(v));

        // An async side effect is awaited on these surfaces too
        Result<int> auditedTask = await GetResultTask().Tap(async v => await AuditAsync(v));
    }

    internal static async Task CancellationSample(Result<int> result, Result command, CancellationToken ct)
    {
        // The token form: the delegate takes the token and returns a ValueTask, as a port does
        Result<string> fetched = await result.BindAsync((v, token) => FetchAsync(v, token), ct);

        // The non-generic Result has the same operations, in both forms
        Result saved = await command.BindAsync(async () => await SaveAsync());
        Result audit = await command.TapAsync(token => AuditCommandAsync(token), ct);
    }

    private static Task<string> TransformAsync(int value) => throw new NotSupportedException();

    private static ValueTask<Result<string>> FetchAsync(int value, CancellationToken ct) => throw new NotSupportedException();

    private static Task<Result> SaveAsync() => throw new NotSupportedException();

    private static ValueTask AuditCommandAsync(CancellationToken ct) => throw new NotSupportedException();

    private static Task<Result<string>> GetNextAsync(int value) => throw new NotSupportedException();

    private static Task<Result<int>> FallbackAsync(IError error) => throw new NotSupportedException();

    private static Task LogAsync(Result<int> result) => throw new NotSupportedException();

    private static Task AuditAsync(int value) => throw new NotSupportedException();

    private static Task<Result<int>> GetResultTask() => throw new NotSupportedException();

    private static ValueTask<Result<int>> GetResultValueTask() => throw new NotSupportedException();

    private static string Transform(int value) => throw new NotSupportedException();

    private static Result<string> GetNext(int value) => throw new NotSupportedException();
}
