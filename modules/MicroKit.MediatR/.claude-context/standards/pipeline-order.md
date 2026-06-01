# Standard: Pipeline Order

**Canonical registry of `PipelineOrder` values for MicroKit.MediatR.**

Every behavior declares `public override int Order => PipelineOrder.{Name};`. The values below are
a **contract** — changing an existing value is a breaking change (`api-reviewer` blocks it).

Adding a new behavior requires:
1. Picking an unused value (see "Reserved Ranges")
2. Adding the constant to `PipelineOrder.cs`
3. Registering it here with a one-line rationale
4. Registering the behavior in DI at the matching ordered position

---

## Built-in Order

| Value | Behavior | Opt-in | Scope | Short-circuits? | Rationale |
|-------|----------|--------|-------|-----------------|-----------|
| `100` | `LoggingBehavior` | none (always) | all | never | Observe everything, including downstream failures |
| `200` | `AuthorizationBehavior` | `IAuthorizedRequest` | all | yes | Fail-fast on security before any work |
| `300` | `ValidationBehavior` | `IValidator<T>` registered | all | yes | Reject bad input before business behaviors |
| `400` | `IdempotencyBehavior` | `IIdempotentCommand` | commands | yes (cache hit) | Dedup commands before execution |
| `500` | `CachingBehavior` | `ICacheableQuery` | queries | yes (cache hit) | Serve reads from cache before the handler |
| `600` | `RetryBehavior` | `IRetryableRequest` | all | no (wraps `next`) | Retry only the handler call |
| `1000` | (handler) | — | — | — | Business code — never a behavior |

## Execution Semantics

- Pre-`next()` code runs **outermost-first** (100 → 600).
- Post-`next()` code unwinds **innermost-first** (600 → 100).
- MediatR runs behaviors in **DI registration order** — `AddMicroKitMediatR` registers them in this sequence.

## Reserved Ranges

| Range | Use |
|-------|-----|
| `100–600` | Built-in behaviors (do not reuse a taken value) |
| `101–599` | Custom behaviors that must interleave with built-ins (e.g., `150` Audit between Logging and Authorization) |
| `601–999` | Custom behaviors that run after Retry but before the handler |
| `< 100` / `> 999` | Reserved — requires written justification + `architect` approval |

## Rules

- No two behaviors share a value.
- A custom behavior between built-ins documents its rationale here and in `PipelineOrder.cs`.
- `IdempotencyBehavior` (400) applies to commands only; `CachingBehavior` (500) to queries only —
  enforced by the marker guard, validated by `/audit-pipeline`.
