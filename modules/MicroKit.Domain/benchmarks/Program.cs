using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using MicroKit.Domain.Benchmarks;

namespace MicroKit.Domain.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance;

        if (args.Contains("--all"))
        {
            // Run all benchmarks
            BenchmarkRunner.Run(typeof(Program).Assembly, config);
        }
        else if (args.Contains("--value-objects"))
        {
            // Run only value object benchmarks
            BenchmarkRunner.Run<ValueObjectBenchmarks>(config);
        }
        else if (args.Contains("--aggregates"))
        {
            // Run only aggregate benchmarks
            BenchmarkRunner.Run<AggregateBenchmarks>(config);
        }
        else if (args.Contains("--events"))
        {
            // Run only event benchmarks
            BenchmarkRunner.Run<DomainEventBenchmarks>(config);
        }
        else
        {
            // Default: run value object benchmarks
            BenchmarkRunner.Run<ValueObjectBenchmarks>(config);
        }
    }
}