# Workflow: Adding a Behavior

Step-by-step guide for adding a new pipeline behavior to `MicroKit.MediatR.Behaviors`.

## When to Use

When a cross-cutting concern (audit, rate-limit, encryption, metrics) should apply to many
requests via the pipeline rather than being copied into each handler.

## Steps

### 1. Confirm It Belongs in the Pipeline

A behavior is justified when the concern is cross-cutting and opt-in via a marker. If only one
handler needs it, put it in the handler. Bring the design to the `behavior-designer` agent.

### 2. Choose the PipelineOrder

Pick a value between existing behaviors (100–600) or 601–999. It must be unique. Register it in
`.claude-context/standards/pipeline-order.md` and add the constant to `PipelineOrder.cs`.

```
100 Logging · 200 Authorization · 300 Validation · 400 Idempotency · 500 Caching · 600 Retry
```

### 3. Scaffold

```
/new-behavior <Name> [--order <n>] [--marker I<Marker>] [--scope all|commands|queries] [--short-circuit]
```

### 4. Implement

- `sealed class` inheriting `BehaviorBase<TRequest, TResponse>` (never `IPipelineBehavior` directly)
- `public override int Order => PipelineOrder.<Name>;`
- **First statement** is the marker guard: `if (request is not IMarker m) return await next().ConfigureAwait(false);`
- `ConfigureAwait(false)` on every await
- For failures: `Result.Failure(...)` when `TResponse` is `Result<T>`, otherwise throw
- Never call `IMediator` from inside the behavior

### 5. Register in DI (at the right position)

MediatR executes behaviors in registration order — register at the slot matching the `PipelineOrder`.

```csharp
cfg.Add<Name>Behavior();   // fluent, inserted at the correct ordered position
```

### 6. Tests (mandatory matrix)

```
/new-handler-tests <Name>Behavior --type behavior
```

- `Handle_WhenMarkerPresent_AppliesLogic`
- `Handle_WhenMarkerAbsent_PassesThrough`
- `Handle_WhenShortCircuit_DoesNotCallNext` (if it can short-circuit)
- `Handle_WhenError_PropagatesCorrectly`

### 7. Performance Review

Behaviors are hot-path. Run `/review-performance --file <path>` and add a benchmark
(`/generate-benchmarks <Name>Behavior`). Confirm the pass-through path allocates ~0.
