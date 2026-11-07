using BenchmarkDotNet.Attributes;
using EO = ErrorOr;
using FR = FluentResults;

namespace MicroKit.Result.PerformanceTests;

// Creating a success and a failure. Each failure reuses a cached error, so the figures are the result's own cost.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CreationBenchmarks
{
    private static readonly CategorizedError MicroKitError = Error.NotFound(ErrorCode.From("ITEM.NOT_FOUND"), "Item not found.");
    private static readonly EO.Error ErrorOrError = EO.Error.NotFound("ITEM.NOT_FOUND", "Item not found.");
    private static readonly FR.Error FluentError = new("Item not found.");

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Success")]
    public Result<int> MicroKit_Success() => Result.Success(42);

    [Benchmark]
    [BenchmarkCategory("Success")]
    public EO.ErrorOr<int> ErrorOr_Success() => 42;

    [Benchmark]
    [BenchmarkCategory("Success")]
    public FR.Result<int> FluentResults_Success() => FR.Result.Ok(42);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Failure")]
    public Result<int> MicroKit_Failure() => Result.Failure<int>(MicroKitError);

    [Benchmark]
    [BenchmarkCategory("Failure")]
    public EO.ErrorOr<int> ErrorOr_Failure() => ErrorOrError;

    [Benchmark]
    [BenchmarkCategory("Failure")]
    public FR.Result<int> FluentResults_Failure() => FR.Result.Fail<int>(FluentError);
}
