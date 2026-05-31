# Context: Design Decisions

**Design rationale for MicroKit.MediatR** — the "why" behind choices that are not full ADRs but
shape day-to-day implementation. For formal, ecosystem-impacting decisions see
`architectural-decisions.md`.

---

## Why a 4-Project Split

| Project | Reason it is separate |
|---------|----------------------|
| `Abstractions` | Consumers depend only on contracts — handlers compile without pulling MediatR, FluentValidation, or Polly |
| `MicroKit.MediatR` (core) | Owns the dispatch/DI wiring and `BehaviorBase`; depends on the MediatR engine |
| `Behaviors` | FluentValidation + Polly are heavy, opinionated deps — confining them here keeps core lean and lets a consumer take the contracts + dispatch without the batteries-included behaviors |
| `Testing` | NSubstitute and harnesses are test-time only — they must never ship inside a runtime package |

The split lets a minimalist consumer reference only `Abstractions` + core, and a batteries-included
consumer add `Behaviors`. `Testing` is a dev dependency.

## Why This Pipeline Order

The order is a security/correctness gradient, outermost to innermost:

1. **Logging (100)** — must wrap everything so failures in auth/validation are still observed.
2. **Authorization (200)** — reject unauthorized requests before spending any effort (fail-fast security).
3. **Validation (300)** — reject malformed input before business behaviors run.
4. **Idempotency (400)** — for commands, dedup before execution (so a replay returns the original result).
5. **Caching (500)** — for queries, serve from cache before hitting the handler.
6. **Retry (600)** — wrap *only* the handler call, so retries don't re-run validation/auth.
7. **Handler (1000)** — business logic.

Idempotency precedes Caching because they target disjoint request kinds (commands vs queries); the
relative order matters only for the rare request that is both, which the marker scope forbids.

## Why Opt-In over Mandatory (except Logging)

Mandatory caching/retry/idempotency would be dangerous-by-default (caching a command's side effects,
retrying a non-idempotent operation). Opt-in via a marker makes the choice explicit and type-checked
at the call site. Logging is mandatory because universal observability is always safe and valuable.
See ADR-004.

## Why ValueTask on Handlers

Handlers frequently complete synchronously — a cache hit, an in-memory projection, or an early
guard-clause failure. `Task<T>` allocates a state-machine box every time; `ValueTask<T>` does not on
the synchronous path. On a pipeline invoked per request, that allocation matters. Notification
handlers stay on `Task` because MediatR's contract requires it. See ADR-003.

## Why Result<T> Is Optional, Not Forced

`Result<T>` is the right tool when failure is a **modeled outcome** (not-found, validation, conflict).
It is ceremony when an operation either succeeds or fails exceptionally. Forcing `Result<Config>` on a
read that cannot fail adds noise. So both `ICommand<Result<T>>` and `ICommand<T>` are first-class; the
behaviors adapt (build a `Result.Failure` for `Result<T>`, throw for `T`).

## Why Behaviors Detect Result<T> vs T (and cache it)

A behavior is generic over `TResponse`. To produce a failure correctly it must know whether
`TResponse` is a `Result<T>`. This is determined once per closed generic type and cached — doing it
per request via reflection would blow the latency budget. `BehaviorBase` owns this logic (ADR-002).

## Why No IMediator in Handlers

Injecting `IMediator` into a handler couples it to the pipeline and invites re-entrant dispatch
(a handler sending another command), which is hard to test and risks infinite loops. Cross-handler
communication goes through domain events (`IDomainEventDispatcher`) — published after persistence,
handled by dedicated handlers. See `.claude/rules/no-handler-coupling.md`.

## Why Domain Events Publish After Persistence

A domain event asserts a fact ("the order was created"). The fact is only true once the write
commits. Publishing before persistence risks handlers reacting to something that then fails to save.
So the order is: build aggregate → persist → publish event → return.
