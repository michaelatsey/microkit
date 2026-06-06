# Template: Logging-Benchmark

Code template for a BenchmarkDotNet benchmark suite.

Used by `/logging-generate-benchmarks` command.

---

## File: `{TargetClass}Benchmarks.cs`

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging;
using MicroKit.Logging.Abstractions;

namespace MicroKit.Logging.Benchmarks;

/// <summary>
/// Benchmarks for <see cref="{TargetClass}"/>.
/// </summary>
/// <remarks>
/// Performance budget (from .claude-context/standards/logging-performance-budget.md):
/// - {Scenario}: ≤ {Budget} bytes allocated, ≤ {LatencyBudget} ns
/// </remarks>
[MemoryDiagnoser]                          // mandatory — allocation is the primary metric
[SimpleJob(RuntimeMoniker.Net10_0)]
[BenchmarkCategory("{TargetClass}")]
public class {TargetClass}Benchmarks
{
    // ── Setup ──────────────────────────────────────────────────────────────
    // All expensive object creation goes here — never in benchmark methods

    private {TargetClass} _sut = null!;
    private IEnrichmentContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize _sut and _context
        // Use realistic data sizes, not toy examples
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Dispose if needed
    }

    // ── Baselines ──────────────────────────────────────────────────────────

    /// <summary>Baseline: no-op path — log level disabled.</summary>
    [Benchmark(Baseline = true)]
    public void {Method}_LevelDisabled()
    {
        // Should be 0 bytes allocated
    }

    // ── Hot Paths ──────────────────────────────────────────────────────────

    /// <summary>{Method} with typical production input.</summary>
    [Benchmark]
    public void {Method}_Typical()
    {
        // Must return a value or use Consume() to prevent dead code elimination
        // Example: return _sut.Execute(_context);
    }

    /// <summary>{Method} under high cardinality (stress scenario).</summary>
    [Benchmark]
    public void {Method}_HighCardinality()
    {
        // Simulate worst-case scenario
    }
}
```
