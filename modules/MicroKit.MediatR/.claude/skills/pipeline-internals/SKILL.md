---
name: pipeline-internals
description: How to reason about MicroKit.MediatR pipeline execution — behavior ordering (Logging 100 → Authorization 200 → Validation 300 → Idempotency 400 → Caching 500 → Retry 600 → handler), short-circuiting, Result<T> vs T response handling, marker opt-in, and DI registration order. Use whenever debugging why a behavior ran (or didn't), adding a behavior, or explaining pipeline flow.
---

# Skill: Reasoning About the Pipeline

How the MicroKit.MediatR pipeline executes, so you can predict and debug behavior. For the
ordering rules, see `.claude/rules/pipeline-behaviors.md`; for the canonical order registry, see
`.claude-context/standards/pipeline-order.md`.

## The Mental Model: Nested Onion

Each behavior wraps the next via `await next()`. The pipeline is a stack of `try`-around-`next`:

```
Logging( Authorization( Validation( Idempotency( Caching( Retry( handler ) ) ) ) ) )
```

- **Pre-`next()` code** runs **outermost-first** (Logging starts before Authorization).
- **Post-`next()` code** unwinds **innermost-first** (Retry finishes before Logging logs the duration).

## The Order (and why)

| Order | Behavior | Opt-in marker | Why here |
|-------|----------|---------------|----------|
| 100 | Logging | none (always) | Observe everything, including auth/validation failures |
| 200 | Authorization | `IAuthorizedRequest` | Fail-fast on security before doing any work |
| 300 | Validation | `IValidator<T>` registered | Reject bad input before business behaviors |
| 400 | Idempotency | `IIdempotentCommand` | Dedup commands before they execute |
| 500 | Caching | `ICacheableQuery` | Serve queries from cache before the handler |
| 600 | Retry | `IRetryableRequest` | Wrap only the handler call in retries |
| 1000 | Handler | — | Your code |

The number is the contract. Changing an existing value is a breaking change.

## How a Behavior Opts In

The **first** statement is the guard. A request without the marker passes straight through with no cost:

```csharp
if (request is not ICacheableQuery cacheable)
    return await next().ConfigureAwait(false);
```

This is why ordering is by *registration*, but applicability is by *marker*. A behavior can sit in
the pipeline yet be a no-op for most requests.

## Short-Circuiting

A behavior short-circuits by **returning without calling `next()`**:

- **Caching** hit → return the cached response, handler never runs.
- **Idempotency** hit → return the stored response, handler never runs.
- **Authorization** fail → return `Result.Failure(...)` (when `TResponse` is `Result<T>`) or throw (when `T` direct).

Once a behavior short-circuits, every inner behavior is skipped, but every **outer** behavior still
runs its post-`next()` code (e.g., Logging still records the result).

## Result<T> vs T — the Two Failure Modes

Behaviors detect whether `TResponse` is a `Result<T>`:

- **`Result<T>`** → produce `Result.Failure(error)` on rejection (no exception).
- **`T` direct** → throw (`ValidationException`, `UnauthorizedAccessException`).

This detection is resolved per closed generic and cached — never via per-request reflection.

## DI Registration = Execution Order

MediatR executes `IPipelineBehavior` in **registration order**. `AddMicroKitMediatR` registers the
built-in behaviors in `PipelineOrder` sequence. A custom behavior added out of order will execute
out of order — register it at the position matching its `PipelineOrder` value.

## Debugging "why did/didn't this behavior run?"

1. Does the request implement the marker? (no marker → guard returns early)
2. Is the behavior registered? (`AddXxxBehavior()` called?)
3. Is the registration order correct relative to `PipelineOrder`?
4. Did an outer behavior short-circuit before reaching it?
5. Is `TResponse` a `Result<T>` (failure as value) or `T` (failure as throw)?

## Never

- Call `IMediator.Send/Publish` from inside a behavior → re-entrant pipeline, possible infinite loop.
- Mutate the request (records are immutable by design).
- Give two behaviors the same `PipelineOrder`.
