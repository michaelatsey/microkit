# MicroKit.MediatR — Module Brain

## 🎯 Purpose

MicroKit.MediatR is an **opinionated CQRS layer on top of MediatR** (Jimmy Bogard). It does not
replace MediatR — it wraps and enriches it with strongly-typed contracts, a deterministic
pipeline of pre-wired behaviors, first-class `MicroKit.Result` integration, DDD domain-event
support, and isolation-first testability.

> **Core principle:** MicroKit adds typed contracts, ordered behaviors, and conventions.
> The MediatR dispatcher, handler resolution, and assembly scanning stay untouched.

```
MediatR (Jimmy Bogard)        ← underlying dispatch engine
    └── MicroKit.MediatR      ← typed CQRS contracts + ordered behaviors + conventions
            └── Your app      ← clean business handlers
```

---

## 🗺️ Navigation

Always load the relevant file before working on a specific concern:

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule file | `implementer` — plan before code |
| Architecture / CQRS decision | `.claude/rules/cqrs-patterns.md` + `.claude-context/context/architectural-decisions.md` | `architect` |
| Adding a handler | `.claude/workflows/adding-handler.md` + `/new-handler` | `implementer` → `handler-test-generator` |
| Adding a behavior | `.claude/workflows/adding-behavior.md` + `/new-behavior` | `behavior-designer` → `performance-reviewer` |
| Adding a provider/integration | `.claude/workflows/adding-provider.md` + `/new-provider` | `implementer` → `dependency-guardian` |
| Adding a domain event | `/new-domain-event` + `.claude/rules/cqrs-patterns.md` | `architect` |
| Performance concern | `.claude/rules/performance.md` + `.claude/skills/pipeline-internals/SKILL.md` | `performance-reviewer` |
| Public API change | `.claude/rules/dependencies.md` (Abstractions) + `.claude/rules/naming.md` | `api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/dependencies.md` + `.claude-context/context/dependency-graph.md` | `dependency-guardian` — auto on `.csproj` edit |
| Generating tests | `.claude/rules/testing.md` + `/new-handler-tests` | `handler-test-generator` |
| Release | `.claude/workflows/releasing-module.md` + `/release` | `release-manager` |
| TransactionBehavior design | `.claude-context/context/transaction-behavior-design.md` | `architect` + `behavior-designer` when implementing Persistence/Messaging |

---

## 🏛️ Module Structure (4 projects)

```
MicroKit.MediatR/
├── src/
│   ├── MicroKit.MediatR.Abstractions/   ← ICommand, IQuery, IEvent, IStreamQuery, markers, LogPropertyNames bridge
│   ├── MicroKit.MediatR/                ← DI registration, dispatch, BehaviorBase, PipelineOrder, IDomainEventDispatcher
│   ├── MicroKit.MediatR.Behaviors/      ← 6 behaviors (Logging, Authorization, Validation, Idempotency, Caching, Retry)
│   └── MicroKit.MediatR.Testing/        ← CommandHandlerTestHarness, QueryHandlerTestHarness, BehaviorTestHarness, DomainEventTestHarness
├── tests/
│   ├── MicroKit.MediatR.UnitTests/
│   ├── MicroKit.MediatR.IntegrationTests/
│   ├── MicroKit.MediatR.ArchitectureTests/
│   └── MicroKit.MediatR.PerformanceTests/
├── benchmarks/
└── samples/
```

---

## 📦 Dependency Graph

```
MicroKit.MediatR.Abstractions      ← MediatR.Contracts, MicroKit.Domain.Abstractions,
        ↑                            MicroKit.Logging.Abstractions, MicroKit.Result
MicroKit.MediatR (core)            ← Abstractions + MediatR
        ↑
MicroKit.MediatR.Behaviors         ← core + FluentValidation + Polly + MicroKit.Logging.Abstractions
MicroKit.MediatR.Testing           ← core + NSubstitute   (sibling of Behaviors — never references it)
```

**Cross-module:** MicroKit.MediatR is a **Level 2** module and may depend on Level 0
(`MicroKit.Result`, `MicroKit.Domain.Abstractions`) and on `MicroKit.Logging.Abstractions`.
The Result dependency is deliberate — see ADR-001. Full graph: `.claude-context/context/dependency-graph.md`.

---

## 📐 CQRS Model (strict)

```
ICommand / ICommand<TResult>     ← mutates state; may return the created id (or Result<TResult>)
IQuery<TResult>                  ← reads state; never mutates
IStreamQuery<TResult>            ← reads state as IAsyncEnumerable<TResult>
IEvent / IDomainEventNotification ← a fact that already happened; no response
```

> **CQS rule:** a command does not return business data; a query does not mutate state.
> Tolerated exception: a command may return the id of the resource it created.
> Full rules: `.claude/rules/cqrs-patterns.md`.

---

## 🔄 Default Pipeline (deterministic order)

```
100  LoggingBehavior        — always on; observes everything; never short-circuits
200  AuthorizationBehavior  — opt-in via IAuthorizedRequest; fail-fast security
300  ValidationBehavior     — opt-in via registered IValidator<T>; collect-all errors
400  IdempotencyBehavior    — opt-in via IIdempotentCommand (commands only)
500  CachingBehavior        — opt-in via ICacheableQuery (queries only)
600  RetryBehavior          — opt-in via IRetryableRequest (Polly)
1000 Handler                — your code
```

Order is guaranteed by the `PipelineOrder` registry. Each behavior is opt-in via an interface
marker (except Logging). Full rules: `.claude/rules/pipeline-behaviors.md`; canonical values:
`.claude-context/standards/pipeline-order.md`.

---

## 🔌 MicroKit.Result Integration (optional per handler)

Handlers may return `Result<T>` **or** `T` directly — both are supported. Behaviors detect which
form `TResponse` takes: on rejection they produce `Result.Failure(...)` for `Result<T>`, or throw
for `T` direct.

```csharp
// Result<T> — for operations that can fail in a modeled way
public sealed class GetUserHandler(IUserReadRepository repo)
    : IQueryHandler<GetUserQuery, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(GetUserQuery q, CancellationToken ct = default)
        => await repo.FindAsync(q.UserId, ct).ConfigureAwait(false) is { } u
            ? Result.Success(u.ToDto())
            : Result.Failure<UserDto>(new UserNotFoundError(q.UserId));
}
```

---

## 🧱 DomainEvent Pattern

```csharp
public sealed record UserRegisteredEvent(Guid UserId, string Email, DateTimeOffset RegisteredAt) : IEvent;

public sealed class UserRegisteredNotification : DomainEventNotification<UserRegisteredEvent>
{
    public UserRegisteredNotification(UserRegisteredEvent domainEvent) : base(domainEvent) { }
}

public sealed class SendWelcomeEmailHandler(IEmailService email)
    : IDomainEventHandler<UserRegisteredEvent, UserRegisteredNotification>
{
    public async Task Handle(UserRegisteredNotification n, CancellationToken ct)
        => await email.SendWelcomeAsync(n.DomainEvent.Email, ct).ConfigureAwait(false);
}
```

Publish events from the command handler **after** persistence, via `IDomainEventDispatcher` —
never from a behavior, never before the write.

---

## 📐 Non-Negotiable Rules

1. **`sealed record`** for commands/queries, **`sealed class`** for handlers and behaviors
2. **`ValueTask<T>`** on Command/Query handlers — never `Task<T>`
3. **`CancellationToken ct = default`** always last
4. **`ConfigureAwait(false)`** on every await in library code
5. **Behaviors inherit `BehaviorBase<TRequest, TResponse>`** — never `IPipelineBehavior` directly
6. **No `IMediator` in handlers** — use `IDomainEventDispatcher`
7. **Marker guard is the first statement** in every behavior (zero-cost pass-through)
8. **Canonical log property names only** — `LogPropertyNames.*` (esp. `CommandName`)
9. **Shouldly + NSubstitute** for tests — **FluentAssertions is banned**
10. **No inline `Version=`** on `PackageReference` — CPM via `Directory.Packages.props`

---

## 🤖 Available Agents

| Agent | Model | Trigger |
|-------|-------|---------|
| `implementer` | Opus | **First agent to invoke** before writing new code — produces a plan and waits for approval |
| `architect` | Opus | CQRS decisions, new contracts, pipeline order, dependency-graph changes |
| `behavior-designer` | Opus | Designing/implementing/testing a pipeline behavior |
| `api-reviewer` | Opus | Public API surface in Abstractions or core — required before merge |
| `performance-reviewer` | Sonnet | Behavior / dispatch hot-path code, benchmark deltas |
| `release-manager` | Sonnet | `/release` — 4-package release lifecycle |
| `handler-test-generator` | Sonnet | `/new-handler-tests` — generates Shouldly + NSubstitute suites |
| `dependency-guardian` | Haiku | Any `.csproj` / project-reference change — fast PASS/BLOCK |

---

## ⚡ Available Commands

| Command | Purpose |
|---------|---------|
| `/new-handler` | Scaffold a command/query/stream/event handler + test skeleton |
| `/new-behavior` | Scaffold a pipeline behavior + marker + DI + test |
| `/new-domain-event` | Scaffold the DomainEvent + Notification + handler triptych |
| `/new-provider` | Scaffold an optional integration/provider project |
| `/new-handler-tests` | Generate a handler/behavior test suite (Shouldly + NSubstitute) |
| `/audit-pipeline` | Audit registered handlers/behaviors for CQRS + ordering violations |
| `/review-architecture` | Run the architect agent against the module |
| `/review-performance` | Run the performance-reviewer agent on hot-path code |
| `/generate-benchmarks` | Generate a BenchmarkDotNet suite |
| `/release` | Prepare and validate a release |

---

## 🔗 Context Layer

Extended intelligence (standards, templates, ADRs) lives in `.claude-context/`:

```
.claude-context/
├── standards/   ← canonical registries (naming, pipeline order, handler contracts, perf budget, taxonomy)
├── templates/   ← code-generation templates (command/query/behavior/test-harness/provider)
└── context/     ← ADRs, dependency graph, ecosystem overview, design decisions
```

These are **not** Claude Code runtime files. Agents and commands load them explicitly when needed.

---

## 🔢 Versioning

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/mediatr-v\\d+\\.\\d+"
  ]
}
```

Git tag convention: `mediatr-v1.0.0`, `mediatr-v1.1.0-beta.1`. All 4 packages share one version.

## 🔗 References
- [MediatR](https://github.com/jbogard/MediatR) · [CQRS — Fowler](https://martinfowler.com/bliki/CQRS.html) · [Domain Events — Udi Dahan](https://udidahan.com/2009/06/14/domain-events-salvation/)
