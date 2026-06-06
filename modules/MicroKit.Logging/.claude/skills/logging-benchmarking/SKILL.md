---

name: logging-benchmarking
description: Use this skill when creating, modifying, reviewing, or validating BenchmarkDotNet benchmarks for MicroKit.Logging, analyzing performance regressions, or evaluating compliance with logging performance budgets.
-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Provide a standardized process for running BenchmarkDotNet benchmarks, measuring memory allocations, validating performance budgets, and interpreting benchmark results for MicroKit.Logging.

## When to Use

Use this skill when:

* Adding a new benchmark to MicroKit.Logging.
* Modifying the logging pipeline, enrichers, providers, sinks, or related infrastructure.
* Investigating a performance regression.
* Reviewing a pull request that affects logging performance.
* Validating compliance with established performance budgets.
* Generating benchmark artifacts for performance tracking.

## Instructions

### 1. Execute Benchmarks

Always run benchmarks in Release mode.

Run all benchmarks:

```bash
dotnet run \
  --project modules/MicroKit.Logging/benchmarks/MicroKit.Logging.Benchmarks/ \
  -c Release
```

Run a specific benchmark category:

```bash
dotnet run \
  --project modules/MicroKit.Logging/benchmarks/ \
  -c Release \
  --filter "*EnrichmentPipeline*"
```

Run benchmarks with explicit memory diagnostics:

```bash
dotnet run \
  --project modules/MicroKit.Logging/benchmarks/ \
  -c Release \
  --filter "*" \
  -- --memory
```

### 2. Validate Benchmark Configuration

Every benchmark class must include memory diagnostics.

Required baseline structure:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[BenchmarkCategory("EnrichmentPipeline")]
public class EnrichmentPipelineBenchmarks
{
    [Benchmark(Baseline = true)]
    public void NoEnrichment()
    {
    }

    [Benchmark]
    public void WithTenantAndCorrelationEnrichers()
    {
    }
}
```

Validation requirements:

* `[MemoryDiagnoser]` is mandatory.
* A meaningful baseline benchmark must exist.
* Benchmarks must target the supported runtime.
* Benchmark categories should reflect the subsystem being measured.
* Hot-path scenarios should be benchmarked separately from baseline scenarios.

### 3. Analyze Results

Prioritize metrics in the following order:

#### Allocated

Bytes allocated per operation.

Requirements:

* No-op paths should allocate zero bytes.
* Unexpected allocations must be investigated before merging.

#### Mean

Average execution time per operation.

Use this metric to evaluate absolute performance.

#### Ratio

Relative performance compared to the baseline benchmark.

Use this metric to quantify the impact of new functionality.

### 4. Validate Performance Budget

Compare benchmark results against the performance targets defined in:

```text
.claude-context/standards/logging-performance-budget.md
```

If a benchmark exceeds its performance budget:

* Identify the source of the regression.
* Document the impact.
* Request review from the designated performance reviewer.

Changes exceeding budget by more than 10% require explicit performance approval.

### 5. Export and Track Results

Export benchmark results when measuring significant changes.

```bash
dotnet run ... -- --exporters csv --artifacts benchmarks/results/
```

Store generated artifacts in:

```text
benchmarks/results/
```

Benchmark artifacts should be committed when:

* Establishing a new baseline.
* Introducing significant performance improvements.
* Documenting a confirmed regression.
* Completing major architectural changes.

## Best Practices

* Benchmark realistic production scenarios.
* Measure hot paths independently.
* Keep benchmark inputs deterministic.
* Avoid benchmarking unrelated concerns in the same test.
* Compare results against a stable baseline.
* Review allocation data before reviewing execution time.
* Maintain historical benchmark artifacts for trend analysis.

## Validation Checklist

* [ ] Benchmarks executed in Release mode.
* [ ] MemoryDiagnoser applied.
* [ ] Baseline benchmark defined.
* [ ] Allocation metrics reviewed.
* [ ] Performance budget validated.
* [ ] Regression impact documented.
* [ ] Benchmark artifacts exported when required.
* [ ] Results committed for significant performance changes.
