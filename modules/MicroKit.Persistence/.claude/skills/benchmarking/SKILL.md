---
name: benchmarking
description: How to write and run BenchmarkDotNet benchmarks for MicroKit.Persistence. Use whenever you need to measure repository overhead, EF Core query cost, allocation per operation, or validate against the performance budget. Allocation per operation is the primary metric.
---

# Skill: Benchmarking

How to benchmark MicroKit.Persistence operations.

## Commands

```bash
# Run all benchmarks
dotnet run --project benchmarks/MicroKit.Persistence.Benchmarks/ -c Release --filter *

# Run specific benchmark class
dotnet run --project benchmarks/ -c Release --filter "*RepositoryBenchmarks*"

# Export to markdown (for PR descriptions)
dotnet run --project benchmarks/ -c Release --filter * --exporters markdown
```

## Benchmark Structure

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class RepositoryBenchmarks
{
    private InMemoryRepository<User> _repo = null!;
    private User _user = null!;

    [GlobalSetup]
    public void Setup()
    {
        _repo = new InMemoryRepository<User>();
        _user = User.Create(UserId.New(), Email.From("bench@test.com"));
    }

    [Benchmark(Baseline = true)]
    public async Task<User?> FindAsync_Baseline()
        => await _repo.FindAsync(_user.Id);

    [Benchmark]
    public async Task AddAndCommit()
    {
        var user = User.Create(UserId.New(), Email.From("x@test.com"));
        await _repo.AddAsync(user);
        await new InMemoryUnitOfWork().CommitAsync();
    }
}
```

## Primary Metrics

| Metric | What it measures |
|--------|-----------------|
| `Mean` | Average execution time |
| `Allocated` | Heap bytes per operation |
| `Gen0` | Gen0 GC collections per 1000 ops |

**Allocated is the primary metric** — zero allocation on the synchronous path is the goal
for hot-path repository operations backed by in-memory or cached data.

## Performance Budget

See `.claude-context/standards/performance-budget.md` for concrete targets.

A PR with > 10% regression on any budget metric requires `performance-reviewer` approval.
