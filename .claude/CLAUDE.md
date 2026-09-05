# MicroKit — Monorepo Root Brain

## 🎯 Vision

MicroKit is an ecosystem of modular, opinionated, production-ready .NET 10+ libraries.
Each module is autonomous, published to NuGet, and designed to compose without friction
in a hexagonal / DDD / CQRS / microservices architecture.

> **Core principle:** each module must stand alone. Integration is a bonus, not a prerequisite.

---

## ⚠️ What this file is not

This file holds **durable rules**. It holds no state.

Module status, published versions, what is merged, what is in progress: none of it lives
here. It lives in the most recent file in `.claude-context/sessions/`, which is produced
at the end of every lot and is therefore the only description of the system that is
current by construction.

If this file and a session trace disagree on a fact, **the trace is right**.

---

## 🗺️ Navigation — Where to find context

Always load the relevant module's `.claude/CLAUDE.md` first when working on a specific
module. This root file provides the global vision and cross-cutting conventions.

### Module map

| Module | Path | `.claude/` |
|--------|------|-----------|
| **MicroKit.Result** | `modules/MicroKit.Result/` | yes |
| **MicroKit.Domain** | `modules/MicroKit.Domain/` | yes |
| **MicroKit.Logging** | `modules/MicroKit.Logging/` | yes |
| **MicroKit.MediatR** | `modules/MicroKit.MediatR/` | yes |
| **MicroKit.Persistence** | `modules/MicroKit.Persistence/` | yes |
| **MicroKit.Tenancy** | `modules/MicroKit.Tenancy/` | yes |
| **MicroKit.Auth** | `modules/MicroKit.Auth/` | yes |
| **MicroKit.Execution.Abstractions** | `modules/MicroKit.Execution.Abstractions/` | — |
| **MicroKit.Messaging** | `modules/MicroKit.Messaging/` | yes |
| **MicroKit.Caching** | `modules/MicroKit.Caching/` | not bootstrapped |
| **MicroKit.Http** | `modules/MicroKit.Http/` | not bootstrapped |
| **MicroKit.Observability** | `modules/MicroKit.Observability/` | not bootstrapped |

Release state, versions and work in progress: see the latest session trace.

### Navigation rules for Claude Code

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + module `.claude/CLAUDE.md` + relevant rule | `microkit-[module]-implementer` — plan before code |
| Architecture / contract decision | `.claude/CLAUDE.md` + module `.claude-context/context/*-architectural-decisions.md` | `microkit-[module]-architect` |
| Cross-module ADR | `.claude/CLAUDE.md` + `.claude-context/context/microkit-architectural-decisions.md` | `microkit-[module]-architect` |
| Public API change | module `rules/*-naming.md` + module `rules/*-architecture.md` | `microkit-[module]-api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/cross-module-references.md` + module dependency graph | `microkit-[module]-dependency-guardian` |
| New module bootstrap | `.claude/skills/new-module-bootstrap.md` | — |
| Writing tests | `.claude/rules/testing-libraries.md` (Shouldly mandatory) | — |
| Release | module `workflows/*-releasing.md` + `/[module]-release` command | `microkit-[module]-release-manager` |
| Transversal build / CI | `.claude/CLAUDE.md` + `.claude/rules/monorepo-conventions.md` | — |
| **Resuming work / starting a lot** | `.claude-context/context/session-handoff.md` + latest session trace | — |

### Agents

Module-scoped: an agent loads only when Claude Code is launched from its module
directory. Naming: `microkit-[module]-[role]`.

| Module | Available roles |
|---|---|
| MicroKit.Messaging | implementer · architect · api-reviewer · dependency-guardian · distributed-context-specialist · release-manager |
| MicroKit.Auth | implementer · architect · api-reviewer · dependency-guardian · release-manager |
| MicroKit.Tenancy | prefix `microkit-tenancy-` — roles not yet inventoried |

Prompt conventions:
- List the files to read first, as an explicit ordered list
- Include the agent file itself in that list
- Claude Code has no direct custom-agent invocation — the agent file is loaded as context

---

## 🏛️ Monorepo Architecture

### Physical structure

```txt
MicroKit/
├── .claude/                          ← global brain (cross-cutting conventions)
│   ├── CLAUDE.md                     ← this file
│   ├── agents/                       ← global agents (release, cross-module)
│   ├── commands/                     ← global commands (/new-module, /release, etc.)
│   ├── hooks/                        ← monorepo hooks (pre-commit global, etc.)
│   ├── rules/                        ← cross-cutting rules
│   └── skills/                       ← global skills (build, versioning, CI)
│
├── .claude-context/
│   ├── sessions/                     ← session traces — AUTHORITY ON CURRENT STATE
│   └── context/
│       ├── microkit-architectural-decisions.md  ← cross-module ADRs
│       └── session-handoff.md                   ← web ↔ Claude Code passing method
│
├── .github/
│   ├── workflows/
│   │   ├── ci-*.yml                  ← per-module CI
│   │   └── release-*.yml             ← per-module release (see Versioning)
│   ├── CODEOWNERS
│   └── pull_request_template.md
│
├── modules/
│   └── MicroKit.*/                   ← one directory per module
│
├── LOT.md                            ← current passing order (gitignored, ephemeral)
├── Directory.Build.props             ← shared props for all projects
├── Directory.Build.targets           ← shared targets
├── Directory.Packages.props          ← NuGet Central Package Management
├── .editorconfig
├── .gitignore
├── global.json                       ← pinned .NET SDK version
├── MicroKit.slnx                     ← root solution (all modules)
└── README.md
```

### Internal structure of each module

```txt
modules/MicroKit.[Module]/
├── .claude/                          ← module brain (independent)
├── .claude-context/                  ← standards, templates, ADRs (loaded by agents)
│   ├── standards/
│   ├── templates/
│   └── context/
├── src/
│   ├── MicroKit.[Module].Abstractions/   ← pure contracts, zero third-party dependency
│   ├── MicroKit.[Module]/                ← core implementation
│   ├── MicroKit.[Module].[Provider]/     ← optional integrations
│   ├── MicroKit.[Module].Analyzers/      ← Roslyn analyzers (optional)
│   └── MicroKit.[Module].Generators/     ← source generators (optional)
├── tests/
│   ├── MicroKit.[Module].UnitTests/
│   ├── MicroKit.[Module].IntegrationTests/
│   ├── MicroKit.[Module].ArchitectureTests/
│   └── MicroKit.[Module].PerformanceTests/
├── samples/
├── benchmarks/
├── README.md
└── MicroKit.[Module].slnx
```

---

## 📦 Inter-module dependencies

### Dependency graph (allowed)

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

### Dependency rules

> An **Abstractions** module never depends on another non-Abstractions module.
> Circular dependencies between modules are **forbidden**.
> Any new inter-module dependency requires an update to this graph.

### Cross-module reference pattern — CIReleaseBuild

> **⚠️ Condemned pattern.** This mechanism is superseded by the unified versioning
> migration (see below). It is documented because the eight existing `release-*.yml`
> workflows still depend on it, and because it is **currently broken**: `CIReleaseBuild=true`
> swaps cross-module `ProjectReference` for `PackageReference`, and the CPM pin trails the
> published version. Do not extend it to new modules. Do not repair it — it is being removed.

```xml
<!-- Local dev: source ProjectReferences -->
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="..." />
</ItemGroup>
<!-- CI/Release: published NuGet packages -->
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
</ItemGroup>
```

> Intra-module references (same module, co-versioned) MUST be unconditional
> `ProjectReference`. The CIReleaseBuild pattern applies ONLY to cross-module dependencies.

See `.claude/rules/cross-module-references.md` for the full pattern.

---

## 🔢 Versioning — migration decided, not yet executed

**Current mechanism (in force, condemned):** each module is versioned independently via
`version.json` (Nerdbank.GitVersioning), released on a tag `[module-kebab]-v[semver]`, by
its own `release-*.yml`.

**Decided target:** unified Microsoft-style versioning — one version for the whole
ecosystem, unconditional `ProjectReference`, `CIReleaseBuild` removed, one release
workflow. It absorbs three defects at once rather than patching eight workflows that
would then be replaced.

**Standing prohibition until the migration lands: tag nothing.** Not Messaging, not any
module. A module `.slnx` contains sibling-module projects needed for restore, so `pack`
emits them and `push` publishes them under the module's tag version, silently — foreign
packages have already shipped this way, masked by `--skip-duplicate`.

The reasoning, the evidence and the migration state live in the session traces.

### Branches

```txt
main              ← always stable, protected
dev               ← continuous integration
feature/*         ← features (feature/result/fix-map, feature/mediatr/add-streaming)
release/*         ← release preparation (release/result-1.2)
fix/*             ← bugfixes (fix/tenancy/parallel-sqlite-flaky-test)
```

---

## 🏗️ Shared build — Directory.Build.props / Directory.Packages.props

```txt
Nullable: enable
ImplicitUsings: enable
LangVersion: latest
TreatWarningsAsErrors: true (Release only)
AnalysisLevel: latest-recommended
NuGet: Central Package Management via Directory.Packages.props
CentralPackageTransitivePinningEnabled: true
```

### Directory.Packages.props structure

```txt
ItemGroup Label="Framework"   ← Microsoft.Extensions.* + Microsoft.AspNetCore.*
ItemGroup Label="MicroKit"    ← ALL MicroKit.* sibling packages (pinned to last published version)
ItemGroup Label="EFCore"      ← third-party EF Core + Npgsql (no MicroKit packages)
ItemGroup Label="MediatR"     ← MediatR + MediatR.Contracts
ItemGroup Label="Validation"  ← FluentValidation
ItemGroup Label="Resilience"  ← Polly
ItemGroup Label="OpenTelemetry"
ItemGroup Label="Testing"     ← xunit, Shouldly, NSubstitute, NetArchTest, BenchmarkDotNet
ItemGroup Label="Analyzers"   ← Roslyn analyzers
ItemGroup Label="Auth"        ← Microsoft.IdentityModel.*, JWT
```

> CPM rule: after every module release, bump its version in `ItemGroup MicroKit` on `dev`
> via a dedicated `chore/cpm-*` branch before starting the next release.

---

## ✅ Global conventions (all modules)

### Code

- `sealed record` for errors/VOs/events/options | `sealed class` for handlers/behaviors/processors
- `ValueTask<T>` async | `ConfigureAwait(false)` in libraries
- `CancellationToken ct = default` always last
- `Console.WriteLine` forbidden → `ILogger<T>`
- XML docs mandatory on all public APIs (`src/` only)
- Zero circular dependencies | `.Abstractions` → only other `.Abstractions`

### Build & packaging

- CPM: all versions in root `Directory.Packages.props`
- `CentralPackageTransitivePinningEnabled=true` — mandatory, prevents transitive version drift
- **Intra-module references**: unconditional `ProjectReference` — NEVER inside `CIReleaseBuild` blocks
- **Cross-module references**: the two-ItemGroup pattern above, condemned — do not extend
- CPM bump after every release: dedicated `chore/cpm-*` branch, PR to `dev` only

### Testing

- **`Shouldly` (MIT) mandatory** — FluentAssertions FORBIDDEN (Xceed commercial license v8+)
- **`NSubstitute`** for mocks | **`NetArchTest`** for architecture tests
- Tests: `GenerateDocumentationFile=false` + `NoWarn CS1591;CA1707`
- **ArchitectureTests mandatory** before any release (empty project = blocking)
- SQLite integration tests: each `Task.Run` must have its own isolated connection
- Testcontainers PostgreSQL for anything touching uniqueness or concurrency

### Runtime invariants

- **BackgroundService**: `IServiceScopeFactory` only in the constructor — never scoped services directly
- **Batch processing**: one `IAsyncServiceScope` per message — never shared across messages
- **Publishers**: silent success FORBIDDEN — throw `InvalidOperationException` if no transport
- **IApplicationEvent**: REJECTED — YAGNI. Do not introduce until a real need exists.

### Bootstrap

- `.claude/` complete BEFORE any implementation

### Event taxonomy (canonical)

```txt
MicroKit.Domain.Events.IEvent          ← canonical root (Domain module)
  IDomainEvent : IEvent                ← domain events (Domain module)
  IIntegrationEvent : IEvent           ← integration events (Messaging module)

MicroKit.MediatR.Events.IEvent         ← [Obsolete] shim → use MicroKit.Domain.Events.IEvent
```

### Domain event dispatch topology (ADR-MEDIATR-009)

```txt
Domain Event  (accumulated on the tracked aggregate)
    │
    ▼ P1  IDomainEventsProvider.DrainDomainEvents()   collect · one pass · not recursive
    │
    ├──► P2 IDomainEventHandler<TEvent>         sync · in-transaction · DI direct · raw event
    │        (bypasses MediatR pipeline behaviors intentionally)
    │
    └──► P3 DomainEventNotification<TEvent>     built via IDomainEventNotificationFactory
                 │                                (null when the event has no mapping)
                 ▼ P4 IOutboxWriter.AddBatchAsync   staged in the SAME transaction
                 │
                 ▼ (outbox processor · at-least-once · after commit)
          INotificationHandler<TNotification>   async · idempotent · technical/integration
```

**Composition — ADR-MEDIATR-014.** One `IDomainEventsDispatcher` implementation
orchestrates the whole sequence. Further in-transaction participants contribute through an
ordered, possibly empty `IEnumerable<IDomainEventsSink>` resolved from DI: MicroKit.MediatR
registers zero sinks, MicroKit.Messaging.MediatR contributes the outbox sink via
`AddMediatRDomainEvents()`. Order-independent by construction — supersedes the
`TryAdd`/`Replace` precedence contract of ADR-MEDIATR-013. **PR #84's core-side `TryAdd`
stays correct and must not be reverted** — it still protects a consumer's own dispatcher.

**Loud failure — ADR-MEDIATR-015.** A `DomainEventNotification<TEvent>` discovered by the
scan with **no** `IDomainEventsSink` registered throws on the first dispatch of a mapped
event, naming the event, the notification and the missing registration. Zero sinks with no
notifications stays valid and free. Register a sink with `TryAddEnumerable` and an
implementation type or instance; a factory lambda is rejected.

> The Messaging outbox model has been rebuilt since ADR-MEDIATR-015 (one reentrant table,
> routing by `MessageKind`, `.MediatR` decorates the standard dispatcher). See ADR-MSG-019
> and the latest session trace.

### Commit conventions

```txt
feat(result): add EnsureAsync overload
fix(mediatr): correct pipeline order with custom behaviors
chore(build): update Directory.Packages.props
docs(domain): add aggregate root design guide
test(tenancy): implement ArchitectureTests
```

---

## 🔁 Working method

The immutable agent flow, the git rules, the post-code agent protocol and the web ↔ Claude
Code passing order are defined **once**, in `.claude-context/context/session-handoff.md`.
They are not restated here.

Before starting any work: read that file, then the most recent file in
`.claude-context/sessions/`.
