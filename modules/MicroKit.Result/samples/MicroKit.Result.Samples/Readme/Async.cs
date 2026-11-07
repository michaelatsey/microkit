namespace MicroKit.Result.Samples.Readme;

// README "Async": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class AsyncSamples
{
    internal static async Task AsyncSample(Result<int> result)
    {
        // Surface 1: async delegate on Result<T>
        Result<string> mapped = await result.MapAsync(async v => await TransformAsync(v));
        Result<string> chained = await result.BindAsync(async v => await GetNextAsync(v));
        Result<int> recovered = await result.CompensateAsync(async e => await FallbackAsync(e));
        Result<int> finished = await result.FinallyAsync(async r => await LogAsync(r));

        // Surface 2: on Task<Result<T>> (sync delegate — awaits the task first)
        Result<string> mappedTask = await GetResultTask().Map(v => Transform(v));
        Result<string> chainedTask = await GetResultTask().Bind(v => GetNext(v));

        // TapAsync, TapErrorAsync and FinallyAsync exist only on Result<T>: await the task first
        Result<int> awaited = await GetResultTask();
        Result<int> audited = await awaited.TapAsync(async v => await AuditAsync(v));

        // Surface 3: on ValueTask<Result<T>> (sync delegate)
        Result<string> mappedValueTask = await GetResultValueTask().Map(v => Transform(v));
    }

    private static Task<string> TransformAsync(int value) => throw new NotSupportedException();

    private static Task<Result<string>> GetNextAsync(int value) => throw new NotSupportedException();

    private static Task<Result<int>> FallbackAsync(IError error) => throw new NotSupportedException();

    private static Task LogAsync(Result<int> result) => throw new NotSupportedException();

    private static Task AuditAsync(int value) => throw new NotSupportedException();

    private static Task<Result<int>> GetResultTask() => throw new NotSupportedException();

    private static ValueTask<Result<int>> GetResultValueTask() => throw new NotSupportedException();

    private static string Transform(int value) => throw new NotSupportedException();

    private static Result<string> GetNext(int value) => throw new NotSupportedException();
}
