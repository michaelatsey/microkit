using BenchmarkDotNet.Attributes;
using EO = ErrorOr;
using FR = FluentResults;

namespace MicroKit.Result.PerformanceTests;

// A Map -> Bind -> Map chain, on the success rail and on the failure rail, with each library's idiomatic API and
// lambdas that capture nothing.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PipelineBenchmarks
{
    private static readonly CategorizedError MicroKitError = Error.NotFound(ErrorCode.From("ITEM.NOT_FOUND"), "Item not found.");
    private static readonly EO.Error ErrorOrError = EO.Error.NotFound("ITEM.NOT_FOUND", "Item not found.");
    private static readonly FR.Error FluentError = new("Item not found.");

    private readonly Result<int> _microKitSuccess = Result.Success(41);
    private readonly Result<int> _microKitFailure = Result.Failure<int>(MicroKitError);
    private readonly EO.ErrorOr<int> _errorOrSuccess = 41;
    private readonly EO.ErrorOr<int> _errorOrFailure = ErrorOrError;
    private readonly FR.Result<int> _fluentSuccess = FR.Result.Ok(41);
    private readonly FR.Result<int> _fluentFailure = FR.Result.Fail<int>(FluentError);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Success")]
    public Result<string> MicroKit_Chain_Success() => _microKitSuccess
        .Map(x => x + 1)
        .Bind(x => x > 0 ? Result.Success(x * 2) : Result.Failure<int>(MicroKitError))
        .Map(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Benchmark]
    [BenchmarkCategory("Success")]
    public EO.ErrorOr<string> ErrorOr_Chain_Success() => _errorOrSuccess
        .Then(x => x + 1)
        .Then(x => x > 0 ? (EO.ErrorOr<int>)(x * 2) : ErrorOrError)
        .Then(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Benchmark]
    [BenchmarkCategory("Success")]
    public FR.Result<string> FluentResults_Chain_Success() => _fluentSuccess
        .Map(x => x + 1)
        .Bind(x => x > 0 ? FR.Result.Ok(x * 2) : FR.Result.Fail<int>(FluentError))
        .Map(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Failure")]
    public Result<string> MicroKit_Chain_Failure() => _microKitFailure
        .Map(x => x + 1)
        .Bind(x => x > 0 ? Result.Success(x * 2) : Result.Failure<int>(MicroKitError))
        .Map(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Benchmark]
    [BenchmarkCategory("Failure")]
    public EO.ErrorOr<string> ErrorOr_Chain_Failure() => _errorOrFailure
        .Then(x => x + 1)
        .Then(x => x > 0 ? (EO.ErrorOr<int>)(x * 2) : ErrorOrError)
        .Then(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Benchmark]
    [BenchmarkCategory("Failure")]
    public FR.Result<string> FluentResults_Chain_Failure() => _fluentFailure
        .Map(x => x + 1)
        .Bind(x => x > 0 ? FR.Result.Ok(x * 2) : FR.Result.Fail<int>(FluentError))
        .Map(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
