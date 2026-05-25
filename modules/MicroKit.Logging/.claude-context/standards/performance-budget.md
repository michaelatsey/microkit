# Standard: Performance Budget

**Allocation and latency targets for MicroKit.Logging hot paths.**

These are the maximum acceptable values. A PR that exceeds these by >10% requires explicit `performance-reviewer` approval.

Baselines are measured on .NET 10.0, Release build, `[MemoryDiagnoser]`, `BenchmarkDotNet`.

---

## Allocation Budgets (bytes per operation)

### Enrichment Pipeline

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| `EnrichmentPipeline.Execute` — log level disabled | **0 bytes** | `IsEnabled` guard prevents all work |
| `EnrichmentPipeline.Execute` — 0 enrichers, level active | **0 bytes** | Nothing to enrich |
| `EnrichmentPipeline.Execute` — 3 enrichers, level active | **≤ 64 bytes** | Scope object only |
| `EnrichmentPipeline.Execute` — 10 enrichers, level active | **≤ 256 bytes** | Acceptable scope overhead |

### Context Access

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| `IOperationContext.CorrelationId` get | **0 bytes** | AsyncLocal read, no allocation |
| `IOperationContext.TenantId` get | **0 bytes** | AsyncLocal read, no allocation |
| `ILogContextAccessor.Current` get | **0 bytes** | Static read |

### Scope Management

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| `BeginOperationScope()` — create | **≤ 128 bytes** | Scope object allocation |
| `BeginOperationScope()` — dispose | **0 bytes** | No allocation on dispose |

### LoggerMessage

| Scenario | Max Allocation | Rationale |
|----------|---------------|-----------|
| `[LoggerMessage]` call — level disabled | **0 bytes** | Delegate check, no allocation |
| `[LoggerMessage]` call — level active, primitive args | **0 bytes** | No boxing with source-generated delegates |

---

## Latency Budgets (nanoseconds per operation)

| Scenario | Max Latency (ns) |
|----------|-----------------|
| `CorrelationId` read via `AsyncLocal` | ≤ 20 ns |
| `Activity.Current.TraceId` read | ≤ 10 ns |
| `EnrichmentPipeline` — level disabled (fast exit) | ≤ 5 ns |
| `EnrichmentPipeline` — 3 enrichers, level active | ≤ 500 ns |
| `BeginOperationScope()` | ≤ 200 ns |

---

## Running the Budget Validation

```bash
dotnet run --project benchmarks/MicroKit.Logging.Benchmarks/ -c Release

# Check allocation column against this file
# Look for: Allocated column in BenchmarkDotNet output
```

## What Causes Budget Violations

| Violation | Common cause |
|-----------|-------------|
| Unexpected string allocation | `$"..."` interpolation in enricher |
| `object[]` allocation | `params` boxing, non-LoggerMessage call |
| Closure allocation | Lambda capturing in enricher registration |
| `AsyncLocal` boxing | Storing struct value as `AsyncLocal<object>` |
| Scope object too large | Dictionary instead of struct-based scope |
