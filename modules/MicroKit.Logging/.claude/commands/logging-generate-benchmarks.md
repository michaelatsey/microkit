# /logging-generate-benchmarks

Generate a BenchmarkDotNet benchmark suite for a target component.

## Usage

```
/logging-generate-benchmarks <TargetClass>
```

**Examples:**
```
/logging-generate-benchmarks EnrichmentPipeline
/logging-generate-benchmarks OperationContextAccessor
/logging-generate-benchmarks TenantLogEnricher
```

## Steps

```
1. Load .claude/skills/logging-benchmarking/SKILL.md
2. Load .claude-context/standards/logging-performance-budget.md
3. Read the target class source file
4. Identify hot-path methods (called per-request or per-log)
5. Generate benchmark class in benchmarks/ directory:
   - [MemoryDiagnoser] always enabled
   - [SimpleJob(RuntimeMoniker.Net10_0)] target runtime
   - [BenchmarkCategory] matching the component
   - Baseline benchmark for comparison
6. Benchmark scenarios:
   - Hot path with enrichment active
   - Hot path with enrichment disabled (no-op)
   - Concurrent access (if async)
   - High-cardinality scope nesting (if applicable)
7. Add to benchmarks/MicroKit.Logging.Benchmarks.csproj
```

## Constraints

- `[MemoryDiagnoser]` is mandatory — allocation is the primary metric
- Use `[GlobalSetup]` for expensive object creation — not in benchmark methods
- Benchmark methods must return a value or use `Consume()` to prevent dead code elimination
- Target from `.claude-context/standards/logging-performance-budget.md` must appear as a comment above each benchmark
