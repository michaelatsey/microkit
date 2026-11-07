namespace MicroKit.Result.Samples.Readme;

// README "Async": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class AsyncSamples
{
    internal static async Task AsyncSample(Result<int> result, Result command, CancellationToken ct)
    {
        // Surface 1: async delegate on Result<T>
        Result<string> mapped = await result.MapAsync(async v => await TransformAsync(v));
        Result<string> chained = await result.BindAsync(async v => await GetNextAsync(v));
        Result<int> recovered = await result.CompensateAsync(async e => await FallbackAsync(e));
        Result<int> finished = await result.FinallyAsync(async r => await LogAsync(r));

        // Surface 2: on Task<Result<T>> (sync delegate — awaits the task first)
        Result<string> mappedTask = await GetResultTask().Map(v => Transform(v));
        Result<string> chainedTask = await GetResultTask().Bind(v => GetNext(v));

        // Tap, TapError and Finally given an async lambda await the side effect, on every surface
        Result<int> audited = await GetResultTask().Tap(async v => await AuditAsync(v));

        // Surface 3: on ValueTask<Result<T>> (sync delegate)
        Result<string> mappedValueTask = await GetResultValueTask().Map(v => Transform(v));

        // CancellationToken form: the delegate takes the token and returns a ValueTask, as a port does
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
