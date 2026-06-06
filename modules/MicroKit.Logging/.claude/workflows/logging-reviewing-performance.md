# Workflow: Logging Reviewing Performance

How to conduct a thorough performance review of a MicroKit.Logging change.

## When to Run

- Before merging any change to the enrichment pipeline
- After any change to `ILogEnricher` implementations
- When benchmark results show >10% allocation regression
- Proactively before a minor/major release

## Steps

### 1. Identify Hot-Path Files

Hot-path files in MicroKit.Logging (called per log statement or per request):
- `EnrichmentPipeline.cs`
- `OperationContextAccessor.cs`
- `*LogEnricher.cs`
- `LogScope*.cs`
- `CorrelationContext.cs`
- `ActivityBridge.cs`

### 2. Run Automated Review

```
/logging-review-performance
```

Or target a specific file:
```
/logging-review-performance --file src/MicroKit.Logging/EnrichmentPipeline.cs
```

### 3. Run Benchmarks

```bash
dotnet run --project benchmarks/ -c Release --filter "*"
```

Compare with the last committed baseline in `benchmarks/results/`.

### 4. Check Allocation Budget

Load `.claude-context/standards/logging-performance-budget.md` for targets.

Key targets:
- `EnrichmentPipeline.Execute` with 3 enrichers: ≤ 0 bytes allocated (when no enrichment needed)
- `BeginOperationScope`: ≤ N bytes (see budget)
- `CorrelationId` access: ≤ 0 allocations

### 5. If Regression Found

1. Use agent logging-performance-reviewer for detailed analysis
2. Apply fixes (prefer `LoggerMessage`, remove closures, add `IsEnabled` guards)
3. Re-run benchmarks to verify
4. Update `benchmarks/results/` baseline
