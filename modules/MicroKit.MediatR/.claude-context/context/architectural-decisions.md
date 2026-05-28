# Context: Architectural Decisions

**ADR (Architecture Decision Records) for MicroKit.MediatR.**

Format: `## ADR-{NNN}: {Title}` · Status: `Accepted` | `Proposed` | `Superseded` | `Deprecated`

---

## ADR-001: MicroKit.MediatR.Abstractions Depends on MicroKit.Result

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

`MicroKit.MediatR.Abstractions` takes a **production dependency** on `MicroKit.Result`. The CQRS
contracts (`ICommand<TResult>`, `IQuery<TResult>`) are routinely closed over `Result<T>`, and the
built-in behaviors produce `Result.Failure(...)` when `TResponse` is a `Result<T>`.

### Rationale

1. **The contracts must express `Result<T>`** — `ICommand<Result<OrderId>>` is the canonical happy
   path. Without the dependency, every consumer would re-declare a bridge type.
2. **Behaviors need to construct failures** — `ValidationBehavior` and `AuthorizationBehavior` return
   `Result.Failure(...)` for `Result<T>` responses. The failure-construction surface lives in `MicroKit.Result`.
3. **The monorepo graph permits it** — MicroKit.MediatR is a Level 2 module; the graph allows
   `MediatR → Result, Domain`. This is the **opposite** of MicroKit.Logging's ADR-006 (which forbids
   a Result dependency) because Logging's enricher contract returns `void` while MediatR's contracts
   are result-bearing.

### Consequences

- `MicroKit.Result` is the only non-`*.Abstractions` MicroKit package that Abstractions may reference.
- Handlers that never fail can still return `T` directly — `Result<T>` is not forced.
- The `dependency-guardian` allowlists `MicroKit.Result` for Abstractions and blocks every other
  concrete MicroKit package.

---

## ADR-002: BehaviorBase Is Mandatory

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

All pipeline behaviors inherit `BehaviorBase<TRequest, TResponse>` (in core) rather than implementing
MediatR's `IPipelineBehavior<TRequest, TResponse>` directly.

### Rationale

- `BehaviorBase` provides the `Order` property that ties a behavior to the `PipelineOrder` registry,
  making ordering explicit and reviewable.
- It centralizes helpers for `Result<T>` vs `T` response detection and failure construction, so each
  behavior does not re-implement reflection-heavy plumbing.
- It gives the `behavior-designer` agent and the `architecture-check` hook a single base type to
  validate against.

### Consequences

- A behavior implementing `IPipelineBehavior` directly is a violation flagged at review.
- The response-type detection is implemented once in `BehaviorBase` and cached per closed generic
  (supports ADR-003's performance goal).

---

## ADR-003: ValueTask over Task on Handlers

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

Command and query handlers return `ValueTask`/`ValueTask<T>`. Notification handlers return `Task`
(MediatR's `INotificationHandler` contract). Stream handlers return `IAsyncEnumerable<T>`.

### Rationale

- Many handlers complete synchronously (cache hit, in-memory projection, guard-clause failure).
  `ValueTask` avoids allocating a `Task` state-machine box on that fast path.
- The pipeline is on every request — eliminating one allocation per dispatch is material at scale.
- Notification handlers cannot change return type without breaking MediatR's contract, so they remain `Task`.

### Consequences

- A Command/Query handler returning `Task<T>` is flagged by `architecture-check` and `performance-check`.
- The performance budget assumes the synchronous path is zero-allocation (see
  `.claude-context/standards/performance-budget.md`).

---

## ADR-004: Behaviors Are Opt-In via Markers (Logging Excepted)

**Status:** Accepted  
**Date:** 2026-05-28  

### Decision

Every behavior except `LoggingBehavior` activates only when the request implements its marker
interface (`IAuthorizedRequest`, `IIdempotentCommand`, `ICacheableQuery`, `IRetryableRequest`) or, for
validation, has a registered `IValidator<T>`. `LoggingBehavior` is always active.

### Rationale

- Imposing caching, retry, or idempotency on every request without consent is surprising and unsafe
  (e.g., caching a command's side effects).
- The marker is a visible, type-checked declaration at the call site — the consumer opts in deliberately.
- Logging is the exception because universal observability has no downside and no risk of incorrect application.

### Consequences

- The first statement of every opt-in behavior is the marker guard (zero-cost pass-through).
- The marker suffix encodes scope (`*Command` ⇒ commands only, `*Query` ⇒ queries only).
- `/audit-pipeline` flags a command marked `ICacheableQuery` or a query marked `IIdempotentCommand`.
