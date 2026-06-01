# /review-performance

Invoke the `performance-reviewer` agent to review dispatch / pipeline hot-path code.

## Usage

```
/review-performance [--file <path>]
```

**Examples:**
```
/review-performance
/review-performance --file src/MicroKit.MediatR.Behaviors/CachingBehavior.cs
```

## What This Command Does

Delegates to the `performance-reviewer` agent with the performance budget loaded.

## Steps

```
1. Load .claude/rules/performance.md
2. Load .claude-context/standards/performance-budget.md
3. Identify hot-path files (behaviors, BehaviorBase, dispatch/send extensions)
4. If --file provided: focus on that file
5. Use agent performance-reviewer to:
   - Confirm ValueTask over Task on handlers
   - Confirm ConfigureAwait(false) on all library awaits
   - Confirm the marker guard is the first statement (zero-cost pass-through)
   - Detect boxing, reflection-per-request, LINQ in Handle, captured closures across next()
   - Confirm Polly pipeline is built once, not per request
   - Confirm CancellationToken propagation; no sync-over-async
6. Output a report with severity per finding (CRITICAL / WARNING / INFO)
7. Recommend benchmarks to run for validation
```

## Hot-Path Files

- `BehaviorBase.cs`
- `*Behavior.cs` (Logging, Authorization, Validation, Idempotency, Caching, Retry)
- `MediatorExtensions.cs` (SendCommand / SendQuery / StreamQuery)
- The dispatch / pipeline assembly code
