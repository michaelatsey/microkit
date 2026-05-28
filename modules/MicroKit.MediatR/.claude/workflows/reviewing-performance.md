# Workflow: Reviewing Performance

How to conduct a thorough performance review of a MicroKit.MediatR change.

## When to Run

- Before merging any change to a behavior or the dispatch path
- After any change to `BehaviorBase` or the send/stream extensions
- When benchmarks show >10% allocation or latency regression
- Proactively before a minor/major release

## Steps

### 1. Identify Hot-Path Files

Called per dispatch:
- `BehaviorBase.cs`
- `*Behavior.cs` (Logging, Authorization, Validation, Idempotency, Caching, Retry)
- `MediatorExtensions.cs` (SendCommand / SendQuery / StreamQuery)
- the dispatch / pipeline assembly code

### 2. Run Automated Review

```
/review-performance
# or target a file:
/review-performance --file src/MicroKit.MediatR.Behaviors/CachingBehavior.cs
```

### 3. Run Benchmarks

```bash
dotnet run --project modules/MicroKit.MediatR/benchmarks/ -c Release --filter "*"
```

Compare with the last committed baseline in `benchmarks/results/`.

### 4. Check the Budget

Load `.claude-context/standards/performance-budget.md`. Key targets:
- Behavior pass-through (marker absent): **0 bytes**
- Synchronous handler via `ValueTask`: no state-machine box
- Full pipeline overhead vs raw MediatR: within budget

### 5. If Regression Found

1. Use agent performance-reviewer for detailed analysis
2. Apply fixes: `ValueTask` over `Task`, `ConfigureAwait(false)`, remove per-request reflection/LINQ,
   cache the Polly pipeline, `LoggerMessage` + `IsEnabled` guards
3. Re-run benchmarks to verify
4. Update `benchmarks/results/` baseline
