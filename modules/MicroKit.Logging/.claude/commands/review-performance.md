# /review-performance

Invoke the `performance-reviewer` agent on a target file or the entire hot-path surface.

## Usage

```
/review-performance [--file <path>] [--run-benchmarks]
```

**Examples:**
```
/review-performance
/review-performance --file src/MicroKit.Logging/EnrichmentPipeline.cs
/review-performance --run-benchmarks
```

## Steps

```
1. Load .claude/rules/performance.md
2. Load .claude-context/standards/performance-budget.md
3. If --file: focus review on that file
4. Else: identify hot-path files:
   - EnrichmentPipeline
   - OperationContext accessor
   - ILogEnricher implementations
   - BeginScope extensions
5. Use agent performance-reviewer to analyze each file
6. If --run-benchmarks:
   dotnet run --project benchmarks/ -c Release --filter *
7. Output violations by severity: CRITICAL / WARNING / INFO
```
