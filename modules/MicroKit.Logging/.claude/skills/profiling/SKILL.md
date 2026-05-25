# Skill: Profiling

How to profile MicroKit.Logging for allocation and CPU hotspots.

## dotnet-trace (CPU + events)

```bash
# Install
dotnet tool install --global dotnet-trace

# Collect a trace during benchmark run
dotnet-trace collect --process-id <PID> \
  --providers Microsoft-DotNETCore-SampleProfiler \
  --output trace.nettrace

# View in PerfView, SpeedScope, or Visual Studio
```

## dotnet-counters (live allocation monitoring)

```bash
dotnet tool install --global dotnet-counters

# Monitor GC allocations live
dotnet-counters monitor --process-id <PID> \
  System.Runtime[gc-allocated-bytes-since-last-gc]
```

## dotMemory / JetBrains (recommended for allocation analysis)

For deep allocation profiling on the enrichment pipeline:
1. Run the benchmark project in Debug (to avoid inlining)
2. Attach JetBrains dotMemory
3. Take snapshots before and after an enrichment call
4. Filter by `MicroKit.Logging` namespace
5. Look for unexpected `string`, `object[]`, or closure allocations

## EventPipe (CI-friendly)

```bash
# Collect allocations during test run
dotnet run --project benchmarks/ -c Release \
  -- --profiler EP
```

## What to Look For

| Pattern | Bad sign |
|---------|---------|
| `string` allocations in `Enrich()` | String interpolation in enricher |
| `object[]` allocations | `params` boxing |
| `<>c__DisplayClass` types | Lambda closures capturing context |
| Repeated `AsyncLocal.Value` boxing | Struct stored in `AsyncLocal<object>` |
