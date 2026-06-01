using MicroKit.MediatR.PerformanceTests.Benchmarks;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.PerformanceTests.SmokeTests;

/// <summary>
/// Verifies that each benchmark method executes without throwing in a non-Release build.
/// CI runs these tests; actual allocation and latency measurements require
/// <c>dotnet run -c Release --project benchmarks/MicroKit.MediatR.Benchmarks/</c>.
/// </summary>
public sealed class BenchmarkSmokeTests
{
    [Fact]
    public void DispatchBenchmarks_Setup_DoesNotThrow()
    {
        var benchmark = new DispatchBenchmarks();
        var ex = Record.Exception(() =>
        {
            benchmark.Setup();
            benchmark.Cleanup();
        });
        ex.ShouldBeNull("benchmark setup/cleanup must not throw");
    }

    [Fact]
    public async Task DispatchBenchmarks_AllMethods_ExecuteWithoutThrowing()
    {
        var benchmark = new DispatchBenchmarks();
        benchmark.Setup();
        try
        {
            await benchmark.RawMediatR_NoMicroKit();
            await benchmark.MicroKit_LoggingBehavior_Only();
            await benchmark.MicroKit_FullPipeline_NoMarkersActive();
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
