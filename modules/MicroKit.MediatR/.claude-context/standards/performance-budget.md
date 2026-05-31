# Standard: Performance Budget

**Allocation and latency targets for MicroKit.MediatR hot paths.**

These are the maximum acceptable values. A PR exceeding them by >10% requires explicit
`performance-reviewer` approval. Baselines are measured on .NET 10.0, Release, `[MemoryDiagnoser]`,
BenchmarkDotNet.

The hot path is **dispatch overhead** — what MicroKit adds on top of invoking the handler directly.

---

## Allocation Budgets (bytes per dispatch)

### Behavior Pass-Through (marker absent)

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| Any opt-in behavior, marker absent | **0 bytes** | Guard returns `next()` before any work |
| `LoggingBehavior`, level disabled | **0 bytes** | `IsEnabled` guard short-circuits |

### Handler Dispatch

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| Synchronous handler via `ValueTask` (cache hit) | **0 bytes** | `ValueTask` avoids the state-machine box |
| `SendCommandAsync` wrapper over raw `IMediator.Send` | **≤ 24 bytes** | Thin typed wrapper only |
| Full pipeline (6 behaviors, all markers absent) overhead vs raw MediatR | **≤ 64 bytes** | Ordered no-op guards only |

### Active Behaviors

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| `ValidationBehavior`, validators pass | **≤ 48 bytes** | Validation context only |
| `CachingBehavior`, cache hit | **≤ 48 bytes** | Deserialized response, no handler call |
| `RetryBehavior`, no retry needed | **≤ 32 bytes** | Reused (cached) Polly pipeline |

---

## Latency Budgets (nanoseconds per dispatch)

| Scenario | Max Latency (ns) |
|----------|-----------------|
| Behavior pass-through (marker absent) | ≤ 10 ns |
| `LoggingBehavior`, level disabled | ≤ 15 ns |
| Full pipeline overhead (6 behaviors, markers absent) vs raw MediatR | ≤ 250 ns |
| `Result<T>` vs `T` response-type detection (cached) | ≤ 5 ns |

---

## Running the Budget Validation

```bash
dotnet run --project modules/MicroKit.MediatR/benchmarks/MicroKit.MediatR.Benchmarks/ -c Release
# Compare the Allocated and Mean columns against this file
```

## What Causes Budget Violations

| Violation | Common cause |
|-----------|-------------|
| Unexpected `Task` box | Handler returns `Task<T>` instead of `ValueTask<T>` |
| `object[]` allocation | `params` boxing / non-LoggerMessage log call |
| Closure allocation | Lambda capturing the request across `await next()` |
| Per-request reflection | Recomputing `Result<T>` detection instead of caching per closed generic |
| Polly pipeline rebuild | Constructing the `ResiliencePipeline` per request instead of once |
| LINQ allocation | `Where`/`Select` inside a behavior `Handle` |
