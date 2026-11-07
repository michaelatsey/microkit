using BenchmarkDotNet.Attributes;

namespace MicroKit.Result.PerformanceTests;

// ErrorCollection.From as shipped (Message built by string.Join over a LINQ Select), and that message algorithm on
// its own. #184 Phase 7 measured a candidate that reads each message once into a pooled buffer and joins with
// string.Create: 11-16 % faster, under the 20 % the phase required to change the path, so the LINQ path stays.
[MemoryDiagnoser]
public class ErrorCollectionBenchmarks
{
    private IError[] _errors = [];

    [Params(3, 10)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup() =>
        _errors = Enumerable.Range(0, Count)
            .Select(i => (IError)ValidationError.Custom($"Field{i}", $"Field{i} is invalid."))
            .ToArray();

    [Benchmark(Baseline = true)]
    public ErrorCollection From_AsShipped() => ErrorCollection.From(_errors);

    [Benchmark]
    public string Message_Linq() => string.Join("; ", _errors.Select(e => e.Message));
}
