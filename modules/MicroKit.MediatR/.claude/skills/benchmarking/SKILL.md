---
name: benchmarking
description: How to write and run BenchmarkDotNet benchmarks for MicroKit.MediatR. Use whenever you need to measure pipeline/dispatch overhead, per-behavior cost, ValueTask vs Task allocation, validate against the performance budget, or compare MicroKit dispatch against raw MediatR. Allocation per dispatch is the primary metric.
---

# Skill: Benchmarking

How to write and run BenchmarkDotNet benchmarks for MicroKit.MediatR.

## Run Benchmarks

```bash
# All benchmarks
dotnet run --project modules/MicroKit.MediatR/benchmarks/MicroKit.MediatR.Benchmarks/ -c Release --filter *

# A single category
dotnet run --project modules/MicroKit.MediatR/benchmarks/MicroKit.MediatR.Benchmarks/ -c Release --filter "*ValidationBehavior*"
```

## Mandatory Attributes

```csharp
[MemoryDiagnoser]                  // allocation per dispatch is the primary metric
[SimpleJob(RuntimeMoniker.Net10_0)]
[BenchmarkCategory("{Component}")]
```

## What to Measure

The hot path is **dispatch overhead** — what MicroKit adds on top of a bare handler call.

| Scenario | Why it matters |
|----------|----------------|
| Marker absent (pass-through) | Behaviors must cost ~0 when not applicable |
| Synchronous handler via `ValueTask` | Confirms no state-machine box on the fast path |
| Asynchronous handler | Realistic async cost |
| Full pipeline (N behaviors) vs raw MediatR | The overhead delta consumers actually pay |

## Rules

- `[GlobalSetup]` builds the DI container / Polly pipeline once — never inside a benchmark method
- Benchmark methods return a value or call `Consume()` to defeat dead-code elimination
- A baseline (`[Benchmark(Baseline = true)]`) is the raw handler or pass-through case
- The relevant target from `.claude-context/standards/performance-budget.md` appears as a comment above each benchmark

## Template

Load `.claude-context/templates/behavior-template.md` and the budget standard, or use `/generate-benchmarks <TargetClass>`.

## Interpreting Results

- Compare `Allocated` against the budget. A regression > 10% requires `performance-reviewer` approval.
- Compare `Mean` (ns/op) for dispatch latency.
- Store baselines in `benchmarks/results/` and diff on each perf-sensitive change.
