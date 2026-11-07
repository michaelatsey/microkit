using BenchmarkDotNet.Attributes;

namespace MicroKit.Result.PerformanceTests;

// The async surface returns ValueTask<Result<T>>. Each benchmark pairs it with the same shape returning
// Task<Result<T>>, with a delegate that completes synchronously and one that truly yields. The benchmarks return the
// ValueTask or Task itself, which BenchmarkDotNet awaits, so no wrapper of the harness is measured.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class AsyncSurfaceBenchmarks
{
    private readonly Result<int> _success = Result.Success(41);

    private static readonly Func<int, Task<int>> CompletedMapper = x => Task.FromResult(x + 1);

    private static readonly Func<int, Task<int>> YieldingMapper = async x =>
    {
        await Task.Yield();
        return x + 1;
    };

    // MapAsync as shipped (ValueTask), and the same operation returning Task.
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MapAsync, sync-completing")]
    public ValueTask<Result<int>> ValueTask_MapAsync_SyncCompleting() => _success.MapAsync(CompletedMapper);

    [Benchmark]
    [BenchmarkCategory("MapAsync, sync-completing")]
    public Task<Result<int>> Task_MapAsync_SyncCompleting() => MapAsTask(_success, CompletedMapper);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MapAsync, truly async")]
    public ValueTask<Result<int>> ValueTask_MapAsync_TrulyAsync() => _success.MapAsync(YieldingMapper);

    [Benchmark]
    [BenchmarkCategory("MapAsync, truly async")]
    public Task<Result<int>> Task_MapAsync_TrulyAsync() => MapAsTask(_success, YieldingMapper);

    // A port awaited by its caller: ValueTask<Result<T>> (the root convention) against Task<Result<T>>.
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Port, sync-completing")]
    public ValueTask<Result<int>> ValueTask_Port_SyncCompleting() => GetValueTaskAsync(41);

    [Benchmark]
    [BenchmarkCategory("Port, sync-completing")]
    public Task<Result<int>> Task_Port_SyncCompleting() => GetTaskAsync(41);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Port, truly async")]
    public ValueTask<Result<int>> ValueTask_Port_TrulyAsync() => GetValueTaskYieldingAsync(41);

    [Benchmark]
    [BenchmarkCategory("Port, truly async")]
    public Task<Result<int>> Task_Port_TrulyAsync() => GetTaskYieldingAsync(41);

    // The shape of AsyncResultExtensions.MapAsync — argument check, then a static local async core — returning Task.
    private static Task<Result<TOut>> MapAsTask<TIn, TOut>(Result<TIn> result, Func<TIn, Task<TOut>> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return Core(result, mapper);

        static async Task<Result<TOut>> Core(Result<TIn> result, Func<TIn, Task<TOut>> mapper) =>
            result.IsSuccess
                ? Result<TOut>.Success(await mapper(result.Value).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
    }

    private static ValueTask<Result<int>> GetValueTaskAsync(int id) => ValueTask.FromResult(Result.Success(id));

    private static Task<Result<int>> GetTaskAsync(int id) => Task.FromResult(Result.Success(id));

    private static async ValueTask<Result<int>> GetValueTaskYieldingAsync(int id)
    {
        await Task.Yield();
        return Result.Success(id);
    }

    private static async Task<Result<int>> GetTaskYieldingAsync(int id)
    {
        await Task.Yield();
        return Result.Success(id);
    }
}
