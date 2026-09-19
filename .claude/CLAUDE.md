# MicroKit

MicroKit is a set of opinionated, production-ready .NET 10 libraries, published to NuGet,
for hexagonal, DDD, CQRS and messaging architectures.
Each module stands alone; integration between modules is optional, never a prerequisite.

## Commands

```bash
dotnet build modules/MicroKit.<Module>/MicroKit.<Module>.slnx -c Release
dotnet test  modules/MicroKit.<Module>/MicroKit.<Module>.slnx -c Release
dotnet build MicroKit.slnx -c Release   # cross-module changes
```

Always `-c Release`: `TreatWarningsAsErrors` applies to Release only.

Launch Claude Code from the repository root for cross-module work, and from
`modules/MicroKit.<Module>/` to work with that module's agents.

## Layout

| Path | Publishable (ADR-GLOBAL-002 D11) |
|---|---|
| `modules/MicroKit.Auth/` | yes |
| `modules/MicroKit.Domain/` | yes |
| `modules/MicroKit.Execution.Abstractions/` | yes |
| `modules/MicroKit.Logging/` | yes |
| `modules/MicroKit.MediatR/` | yes |
| `modules/MicroKit.Messaging/` | yes |
| `modules/MicroKit.Persistence/` | yes |
| `modules/MicroKit.Result/` | yes |
| `modules/MicroKit.Tenancy/` | yes |
| `modules/MicroKit.Caching/` | no — planned module |
| `modules/MicroKit.Http/` | no — planned module |
| `modules/MicroKit.Observability/` | no — planned module |

`MicroKit.Domain.Benchmarks` is outside the publishable perimeter too.

Project and package names inside a module:

- `MicroKit.<Module>.Abstractions` — contracts only
- `MicroKit.<Module>` — core implementation
- `MicroKit.<Module>.<Provider>` — optional integration
- `MicroKit.<Module>.Testing` — test helpers
- `MicroKit.<Module>.Analyzers` — Roslyn analyzers
- Tests under `tests/`: `MicroKit.<Module>.UnitTests`, `.IntegrationTests`, `.ArchitectureTests`,
  `.PerformanceTests`

The root namespace is the package name (`MicroKit.MediatR.Behaviors`), never with `.Core`.

## Dependency graph (allowed)

```txt
MicroKit.Domain                    ← no dependency on other modules
                                     IEvent: canonical root for all event taxonomies
                                     IDomainEvent : IEvent (in Domain)
MicroKit.Result                    ← no dependency on other modules
MicroKit.Execution.Abstractions    ← no dependency on other modules (DI.Abstractions only)
                                     ADR-EXEC-001: cross-cutting Level 0 — IExecutionScopeFactory,
                                     IExecutionContext. NOT a god-package.
MicroKit.Logging                   ← ADR-006: does NOT depend on Result (permanent)
MicroKit.Observability             ← may depend on Result, Logging
MicroKit.Auth                      ← may depend on Result, Domain
MicroKit.Caching                   ← may depend on Result
MicroKit.Persistence               ← may depend on Result, Domain
MicroKit.Messaging                 ← may depend on Result, Persistence (outbox/inbox EFCore),
                                     Execution.Abstractions (ADR-EXEC-001)
                                     ADR-MSG-001: does NOT depend on Domain (IIntegrationEvent standalone)
                                     ADR-EXEC-001: does NOT depend on Tenancy (inversion via
                                     IExecutionScopeFactory — Tenancy implements, host composes)
                                     ADR-MSG-009: MicroKit.Messaging.MediatR is the ONLY Messaging
                                     package allowed to reference MediatR/MediatR.Contracts
MicroKit.Http                      ← may depend on Result, Observability
MicroKit.MediatR                   ← may depend on Result, Domain, Logging.Abstractions,
                                     Persistence.Abstractions (ADR-MEDIATR-011 — TransactionBehavior
                                     requires ITransactionalContext)
MicroKit.Tenancy                   ← may depend on Result, Auth, Persistence,
                                     Execution.Abstractions (tenant-aware IExecutionScopeFactory impl)
```

- An `.Abstractions` project depends only on other `.Abstractions` projects.
- Circular dependencies between modules are forbidden.
- A new inter-module dependency updates this graph.

This graph and the edges the `.csproj` files declare disagree; reconciliation is tracked in #151.

## Code conventions

- `sealed record` for errors, value objects, events and options; `sealed class` for handlers,
  behaviors and processors
- `ValueTask<T>` for async; `ConfigureAwait(false)` in library code
- `CancellationToken ct = default`, always the last parameter
- `Console.WriteLine` is forbidden; log through `ILogger<T>`
- XML docs on every public API in `src/`

Runtime invariants:

- `BackgroundService`: only `IServiceScopeFactory` in the constructor, never a scoped service
- Batch processing: one `IAsyncServiceScope` per message, never shared across messages
- Publishers never succeed silently: with no transport, throw `InvalidOperationException`
- `IApplicationEvent` is rejected (YAGNI); do not introduce it until a real need exists

## Build and packaging

- Central Package Management: every version lives in the root `Directory.Packages.props`, with
  `CentralPackageTransitivePinningEnabled=true`; no `Version=` in a `.csproj`.
- References inside a module are always unconditional `ProjectReference`, in every build mode
  (ADR-GLOBAL-002 D5).
- Versioning and releases are defined by ADR-GLOBAL-002.
- No `[Obsolete]` member is added before `2.0.0`; a known break ships outright on `preview.*`
  (ADR-GLOBAL-002 D10).

## Git and workflow (ADR-GLOBAL-003)

- The GitHub issue is the spec: start from `gh issue view <N>`.
- Branch from `main` as `<type>/<scope>/<issue>-<slug>`, e.g. `fix/messaging/124-tenant-row-stall`.
  `<scope>` is the issue's `mod:` label suffix. One branch per issue, never reused once merged.
- Commits follow Conventional Commits, `<type>(<scope>): <subject>`, with `<type>` one of
  `feat fix perf refactor test docs chore build ci`; `!` before the colon marks a break.
- Claude Code commits on its topic branch. It never pushes, and never opens or merges a pull
  request.
- Post-code reviews run in a fresh context, with the module's agents:
  - `api-reviewer` when the public surface changes
  - `dependency-guardian` when a `.csproj` changes
  - `distributed-context-specialist` for `AsyncLocal`, scoping or workers
- Anything wrong found outside the issue's spec is reported, never fixed in the same branch; it
  becomes its own issue.

## Where things live

- Cross-module ADRs: `.claude-context/context/architecture/decisions/`. Module ADRs live in each
  module's `.claude-context/`.
- Owed work: GitHub issues, written per `.claude-context/context/issues-convention.md`.
- The evidence behind ADR-GLOBAL-002 (versioning, release and the declared dependency graph as
  observed): `.claude-context/context/architecture/versioning-audit.md`.
- Module brains: `modules/MicroKit.<Module>/.claude/CLAUDE.md`.
