# Rule: Performance — MicroKit.MediatR

Every command, query, and event in a consuming application flows through this pipeline.
A behavior is invoked on every matching request, and the pipeline wraps every dispatch.
Overhead here is multiplied by request rate — it must be invisible.

## Mandatory Rules (always enforced)

### ValueTask over Task

- **Handlers return `ValueTask<T>`** (Command/Query). A handler that completes synchronously
  (cache hit, in-memory lookup) allocates **zero** state-machine box with `ValueTask`, whereas
  `Task<T>` allocates one every call.
- Notification handlers return `Task` — this is the MediatR `INotificationHandler` contract and is unavoidable.
- Stream handlers return `IAsyncEnumerable<T>` with `[EnumeratorCancellation]`.

```csharp
// ✅
public async ValueTask<Result<UserDto>> Handle(GetUserQuery query, CancellationToken ct = default)

// ❌ allocates a Task box even on the synchronous fast path
public async Task<Result<UserDto>> Handle(...)
```

### No Boxing / No Reflection on the Hot Path

- Behavior `Handle` must not box value types via `params object[]` — use `LoggerMessage` source-generated delegates for logging.
- Type inspection (is this `TResponse` a `Result<T>`?) is resolved **once per closed generic** and cached — never recomputed per request via reflection.
- `is`-pattern marker guards are cheap and allowed; `typeof(...).GetGenericArguments()` per request is not.

### Marker Guard First

The opt-in guard is the **first statement** in every behavior. Requests that do not implement
the marker must pass through with near-zero cost:

```csharp
public override async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
{
    if (request is not ICacheableQuery cacheable)
        return await next().ConfigureAwait(false);   // pass-through: no allocation
    // ...
}
```

### ConfigureAwait(false)

Every `await` in library code (`MicroKit.MediatR`, `MicroKit.MediatR.Behaviors`) uses `.ConfigureAwait(false)`. This avoids capturing and posting back to a synchronization context.

### LoggerMessage in the LoggingBehavior

The `LoggingBehavior` is on every request. Use the `[LoggerMessage]` source generator — never
`logger.LogInformation($"...")` interpolation, never `params object[]` overloads on the hot path.
Guard expensive log state with `ILogger.IsEnabled(level)`.

### Polly Pipeline Reuse

`RetryBehavior` builds its `ResiliencePipeline` **once** (cached per request type / static), never per dispatch. Constructing a Polly pipeline per request defeats the purpose.

### Async Correctness

- Propagate `CancellationToken` to every async call inside `Handle`.
- Never `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` — sync-over-async causes thread-pool starvation and deadlocks.

## Performance Budget

See `.claude-context/standards/performance-budget.md` for concrete targets (dispatch latency, per-behavior overhead, allocation per request).

## Verification

```bash
dotnet run --project benchmarks/MicroKit.MediatR.Benchmarks/ -c Release --filter *
```

A PR that regresses the dispatch latency or allocation budget by more than 10% requires `performance-reviewer` approval.
