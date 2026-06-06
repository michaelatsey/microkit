# Rule: Logging Performance

MicroKit.Logging is invoked on every log statement in every service. Performance is a first-class constraint, not an optimization.

## Mandatory Rules (always enforced)

### Hot-Path Allocation

- **No string interpolation in log messages** — use `LoggerMessage` delegates or structured templates with `{placeholder}` syntax
- **`LoggerMessage` for all frequently-called log definitions** — compile-time delegate generation, zero boxing
- **No `params object[]`** on hot paths — `LoggerMessage` eliminates this
- **No closures capturing large objects** in enrichers
- **No LINQ** (`Where`, `Select`, `ToList`, etc.) in enrichers or the enrichment pipeline

### AsyncLocal

- `AsyncLocal<T>` instances must be `static readonly` fields
- Values must be immutable or replaced atomically — never mutated in-place
- Document every `AsyncLocal` usage with a comment: purpose + propagation scope
- Maximum 3 levels of `AsyncLocal` nesting without documented justification

### Enrichment

- **Lazy evaluation mandatory** — guard with `ILogger.IsEnabled(LogLevel)` before computing property values
- Enricher `Enrich` methods must not allocate on the "nothing to enrich" path
- Scope objects must be cached where possible — avoid `new` per log call

### Activity / Tracing

- `Activity.Current` access must be cached within an operation boundary — do not call repeatedly in a loop
- `ActivitySource.StartActivity()` only called when `ActivitySource.HasListeners()` returns true

## Performance Budget

See `.claude-context/standards/logging-performance-budget.md` for concrete targets (ns/op, bytes/op).

## Verification

Run benchmarks before and after any change to hot-path code:

```bash
dotnet run --project benchmarks/MicroKit.Logging.Benchmarks/ -c Release --filter *
```

A PR that regresses the allocation budget by more than 10% requires logging-performance-reviewer approval.
