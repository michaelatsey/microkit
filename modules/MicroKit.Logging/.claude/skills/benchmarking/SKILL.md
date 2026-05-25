# Skill: Benchmarking

How to run BenchmarkDotNet benchmarks for MicroKit.Logging and interpret results.

## Run Benchmarks

```bash
# Run all benchmarks (Release mode mandatory)
dotnet run --project modules/MicroKit.Logging/benchmarks/MicroKit.Logging.Benchmarks/ -c Release

# Run specific benchmark class
dotnet run --project modules/MicroKit.Logging/benchmarks/ -c Release \
  --filter "*EnrichmentPipeline*"

# Run with memory diagnoser output explicitly
dotnet run --project modules/MicroKit.Logging/benchmarks/ -c Release \
  --filter "*" -- --memory
```

## Required Diagnosers

Every benchmark class must have `[MemoryDiagnoser]`. This is non-negotiable — allocation is the primary metric for this library.

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[BenchmarkCategory("EnrichmentPipeline")]
public class EnrichmentPipelineBenchmarks
{
    // Baseline: no enrichers
    [Benchmark(Baseline = true)]
    public void NoEnrichment() { ... }

    // Hot path: N enrichers active
    [Benchmark]
    public void WithTenantAndCorrelationEnrichers() { ... }
}
```

## Reading Results

Focus on:
- **Allocated** column — bytes allocated per operation (must be 0 on no-op paths)
- **Mean** — nanoseconds per operation
- **Ratio** — compare to baseline

## Performance Budget

See `.claude-context/standards/performance-budget.md` for targets. A PR that exceeds budget by >10% requires `performance-reviewer` approval.

## Exporting Results

```bash
# Export to CSV for tracking
dotnet run ... -- --exporters csv --artifacts benchmarks/results/
```

Commit benchmark result artifacts to `benchmarks/results/` on significant changes.
