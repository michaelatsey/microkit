# /generate-benchmarks

Generate a BenchmarkDotNet benchmark suite for a target component (behavior or dispatch path).

## Usage

```
/generate-benchmarks <TargetClass>
```

**Examples:**
```
/generate-benchmarks ValidationBehavior
/generate-benchmarks PipelineDispatch
/generate-benchmarks CachingBehavior
```

## Steps

```
1. Load .claude/skills/benchmarking/SKILL.md
2. Load .claude-context/standards/performance-budget.md
3. Read the target class source file
4. Identify hot-path methods (Handle on a behavior, or the dispatch path)
5. Generate a benchmark class in benchmarks/:
   - [MemoryDiagnoser] always enabled
   - [SimpleJob(RuntimeMoniker.Net10_0)] target runtime
   - [BenchmarkCategory] matching the component
   - A baseline benchmark for comparison
6. Benchmark scenarios:
   - Marker absent (pass-through) — should be ~0 alloc
   - Marker present, synchronous handler (ValueTask fast path)
   - Marker present, asynchronous handler
   - Full pipeline of N behaviors vs raw MediatR dispatch (overhead delta)
7. Add to benchmarks/MicroKit.MediatR.Benchmarks.csproj
```

## Constraints

- `[MemoryDiagnoser]` is mandatory — allocation per dispatch is the primary metric
- Use `[GlobalSetup]` for expensive object creation (DI container, Polly pipeline) — never inside benchmark methods
- Benchmark methods must return a value or use `Consume()` to prevent dead-code elimination
- The relevant target from `.claude-context/standards/performance-budget.md` must appear as a comment above each benchmark
