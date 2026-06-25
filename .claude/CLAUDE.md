# MicroKit — Monorepo Root Brain

## 🎯 Vision

MicroKit is an ecosystem of modular, opinionated, production-ready .NET 10+ libraries.
Each module is autonomous, independently versioned, published to NuGet, and designed to
compose without friction in a hexagonal / DDD / CQRS / microservices architecture.

> **Core principle:** each module must stand alone. Integration is a bonus, not a prerequisite.

---

## 🗺️ Navigation — Where to find context

Always load the relevant module's `.claude/CLAUDE.md` first when working on a specific module.
This root file provides the global vision and cross-cutting conventions.

### Module map

| Module | Path | .claude/ | Status |
|--------|------|----------|--------|
| **MicroKit.Result** | `modules/MicroKit.Result/` | `modules/MicroKit.Result/.claude/` | ✅ Released 1.0.0-preview.2 |
| **MicroKit.Domain** | `modules/MicroKit.Domain/` | `modules/MicroKit.Domain/.claude/` | ✅ Released 1.0.0-preview.5 |
| **MicroKit.Logging** | `modules/MicroKit.Logging/` | `modules/MicroKit.Logging/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.MediatR** | `modules/MicroKit.MediatR/` | `modules/MicroKit.MediatR/.claude/` | ✅ Released 1.0.0-preview.1 — redesign preview.2 in progress (fix/messaging/mediatr) |
| **MicroKit.Persistence** | `modules/MicroKit.Persistence/` | `modules/MicroKit.Persistence/.claude/` | ✅ Released 1.0.0-preview.2 |
| **MicroKit.Multitenancy** | `modules/MicroKit.Multitenancy/` | `modules/MicroKit.Multitenancy/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Auth** | `modules/MicroKit.Auth/` | `modules/MicroKit.Auth/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Execution.Abstractions** | `modules/MicroKit.Execution.Abstractions/` | — | ✅ Merged dev — not yet released |
| **MicroKit.Messaging** | `modules/MicroKit.Messaging/` | `modules/MicroKit.Messaging/.claude/` | 🚧 In progress — Abstractions ✅ · Core ✅ · EntityFrameworkCore ✅ · MediatR glue ✅ — all merged dev · fix/messaging/mediatr in progress |
| **MicroKit.Caching** | `modules/MicroKit.Caching/` | `modules/MicroKit.Caching/.claude/` | 📋 Planned |
| **MicroKit.Http** | `modules/MicroKit.Http/` | `modules/MicroKit.Http/.claude/` | 📋 Planned |
| **MicroKit.Observability** | `modules/MicroKit.Observability/` | `modules/MicroKit.Observability/.claude/` | 📋 Planned |

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
│   ├── sessions/                     ← session summaries (read the most recent)
│   └── context/
│       └── microkit-architectural-decisions.md  ← cross-module ADRs
│
├── .github/
│   ├── workflows/
│   │   ├── ci-*.yml                  ← per-module CI
│   │   └── release-*.yml            ← per-module release
│   ├── CODEOWNERS
│   └── pull_request_template.md
│
├── modules/
│   ├── MicroKit.Result/
│   ├── MicroKit.Domain/
│   ├── MicroKit.Logging/
│   ├── MicroKit.MediatR/
│   ├── MicroKit.Persistence/
│   ├── MicroKit.Multitenancy/
│   ├── MicroKit.Auth/
│   ├── MicroKit.Execution.Abstractions/
│   ├── MicroKit.Messaging/
│   └── ...
│
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
                                     ADR-EXEC-001: does NOT depend on Multitenancy (inversion via
                                     IExecutionScopeFactory — Multitenancy implements, host composes)
                                     ADR-MSG-009: MicroKit.Messaging.MediatR is the ONLY Messaging
                                     package allowed to reference MediatR/MediatR.Contracts
MicroKit.Http                      ← may depend on Result, Observability
MicroKit.MediatR                   ← may depend on Result, Domain, Logging.Abstractions,
                                     Persistence.Abstractions (ADR-MEDIATR-011 — TransactionBehavior requires ITransactionalContext)
                                     ADR-MEDIATR-009: two disjoint pipelines —
                                     IDomainEventHandler<TEvent> (sync, in-transaction, DI direct) and
                                     INotificationHandler<TNotification> (async, via outbox, at-least-once)
                                     IDomainEventHandler<TEvent> constrained to where TEvent : IDomainEvent
MicroKit.Multitenancy              ← may depend on Result, Auth, Persistence,
                                     Execution.Abstractions (tenant-aware IExecutionScopeFactory impl)
```

### Dependency rules

> An **Abstractions** module never depends on another non-Abstractions module.
> Circular dependencies between modules are **forbidden**.
> Any new inter-module dependency requires an update to this graph.

### Cross-module pattern for NuGet publish (CIReleaseBuild)

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

See `.claude/rules/cross-module-references.md` for the full mandatory pattern.

---

## 🔢 Versioning — Nerdbank.GitVersioning

Each module is versioned **independently** via `version.json` in its directory.

### Git tag convention for releases

```txt
result-v1.0.0-preview.1            → MicroKit.Result release
domain-v1.0.0-preview.1            → MicroKit.Domain release
logging-v1.0.0-preview.1           → MicroKit.Logging release
mediatr-v1.0.0-preview.1           → MicroKit.MediatR release
persistence-v1.0.0-preview.1       → MicroKit.Persistence release
multitenancy-v1.0.0-preview.1      → MicroKit.Multitenancy release
auth-v1.0.0-preview.1              → MicroKit.Auth release
execution-abstractions-v1.0.0-...  → MicroKit.Execution.Abstractions release
messaging-v1.0.0-preview.1         → MicroKit.Messaging release
```

### Branches

```txt
main              ← always stable, protected
dev               ← continuous integration
feature/*         ← features (scope: result/fix-map, mediatr/add-streaming)
release/*         ← release preparation (release/result-1.2)
fix/*             ← bugfixes (fix/multitenancy/parallel-sqlite-flaky-test)
```

---

## 🏗️ Shared build — Directory.Build.props

```xml
Nullable: enable
ImplicitUsings: enable
LangVersion: latest
TreatWarningsAsErrors: true (Release only)
AnalysisLevel: latest-recommended
NuGet: Central Package Management via Directory.Packages.props
```

---

## ✅ Global conventions (all modules)

### Non-negotiable rules

- `sealed record` for errors/VOs/events/options | `sealed class` for handlers/behaviors/processors
- `ValueTask<T>` async | `ConfigureAwait(false)` in libraries
- `CancellationToken ct = default` always last
- `Console.WriteLine` forbidden → `ILogger<T>`
- Zero circular dependencies | `.Abstractions` → only other `.Abstractions`
- Tests: `GenerateDocumentationFile=false` + `NoWarn CS1591;CA1707`
- CPM: all versions in root `Directory.Packages.props`
- **`Shouldly` (MIT) mandatory** — FluentAssertions FORBIDDEN (Xceed commercial license v8+)
- **`NSubstitute`** for mocks
- **`NetArchTest`** for architecture tests
- `.claude/` complete BEFORE any implementation
- **Cross-module references**: canonical two-ItemGroup CIReleaseBuild pattern mandatory
- **ArchitectureTests mandatory** before any release (empty project = blocking)
- **Integration tests SQLite**: each `Task.Run` must have its own isolated connection
- **BackgroundService**: `IServiceScopeFactory` only in constructor — never scoped services directly
- **Batch processing**: one `IAsyncServiceScope` per message — never shared across messages
- **Publishers**: silent success FORBIDDEN — throw `InvalidOperationException` if no transport
- **Post-code agents**: distributed-context-specialist → dependency-guardian → api-reviewer — mandatory before any merge, in separate Claude Code sessions, always include "Do not commit anything"
- **IApplicationEvent**: REJECTED — YAGNI, no use case. Do not introduce until a real need exists.

### Event taxonomy (canonical)

```txt
MicroKit.Domain.Events.IEvent          ← canonical root (Domain module)
  IDomainEvent : IEvent                ← domain events (Domain module)
  IIntegrationEvent : IEvent           ← integration events (Messaging module)

MicroKit.MediatR.Events.IEvent         ← [Obsolete] shim → use MicroKit.Domain.Events.IEvent
```

### Domain event dispatch topology (ADR-MEDIATR-009)

```txt
Domain Event
    │
    ├──► P3 IDomainEventHandler<TEvent>         sync · in-transaction · DI direct
    │        (bypasses MediatR pipeline behaviors intentionally)
    │
    └──► P4 DomainEventNotification<TEvent>
                 │
                 ▼ (outbox · at-least-once · after commit)
          INotificationHandler<TNotification>   async · idempotent · technical/integration
```

### Commit conventions

```txt
feat(result): add EnsureAsync overload
fix(mediatr): correct pipeline order with custom behaviors
chore(build): update Directory.Packages.props
docs(domain): add aggregate root design guide
test(multitenancy): implement ArchitectureTests
```

### Published NuGet package names

```txt
MicroKit.Result                                        ✅ 1.0.0-preview.2
MicroKit.Result.AspNetCore                             ✅ 1.0.0-preview.1
MicroKit.Domain                                        ✅ 1.0.0-preview.5
MicroKit.Logging                                       ✅ 1.0.0-preview.1
MicroKit.Logging.Abstractions                          ✅ 1.0.0-preview.1
MicroKit.Logging.OpenTelemetry                         ✅ 1.0.0-preview.1
MicroKit.Logging.AspNetCore                            ✅ 1.0.0-preview.1
MicroKit.Logging.Diagnostics                           ✅ 1.0.0-preview.1
MicroKit.Logging.Analyzers                             ✅ 1.0.0-preview.1
MicroKit.Logging.Generators                            ✅ 1.0.0-preview.1
MicroKit.MediatR                                       ✅ 1.0.0-preview.1 → 🚧 preview.2 pending (fix/messaging/mediatr)
MicroKit.MediatR.Abstractions                          ✅ 1.0.0-preview.1 → 🚧 preview.2 pending
MicroKit.MediatR.Behaviors                             ✅ 1.0.0-preview.1 → 🚧 preview.2 pending
MicroKit.MediatR.Testing                               ✅ 1.0.0-preview.1 → 🚧 preview.2 pending
MicroKit.Persistence.Abstractions                      ✅ 1.0.0-preview.2
MicroKit.Persistence                                   ✅ 1.0.0-preview.2
MicroKit.Persistence.EntityFrameworkCore               ✅ 1.0.0-preview.2
MicroKit.Persistence.EntityFrameworkCore.PostgreSql    ✅ 1.0.0-preview.2
MicroKit.Persistence.EntityFrameworkCore.SqlServer     ✅ 1.0.0-preview.2
MicroKit.Persistence.Specifications                    ✅ 1.0.0-preview.2
MicroKit.Persistence.Testing                           ✅ 1.0.0-preview.2
MicroKit.Persistence.Analyzers                         ✅ 1.0.0-preview.2
MicroKit.Multitenancy.Abstractions                     ✅ 1.0.0-preview.1
MicroKit.Multitenancy                                  ✅ 1.0.0-preview.1
MicroKit.Multitenancy.AspNetCore                       ✅ 1.0.0-preview.1
MicroKit.Multitenancy.EntityFrameworkCore              ✅ 1.0.0-preview.1
MicroKit.Multitenancy.Analyzers                        ✅ 1.0.0-preview.1
MicroKit.Auth.Abstractions                             ✅ 1.0.0-preview.1
MicroKit.Auth                                          ✅ 1.0.0-preview.1
MicroKit.Auth.AspNetCore                               ✅ 1.0.0-preview.1
MicroKit.Auth.Permissions                              ✅ 1.0.0-preview.1
MicroKit.Auth.Roles                                    ✅ 1.0.0-preview.1
MicroKit.Auth.Jwt                                      ✅ 1.0.0-preview.1
MicroKit.Auth.Supabase                                 ✅ 1.0.0-preview.1
MicroKit.Auth.Multitenancy                             ✅ 1.0.0-preview.1
MicroKit.Auth.Testing                                  ✅ 1.0.0-preview.1
MicroKit.Execution.Abstractions                        ✅ Merged dev — not yet released
MicroKit.Messaging.Abstractions                        ✅ Merged dev — not yet released
MicroKit.Messaging                                     ✅ Merged dev — not yet released
MicroKit.Messaging.EntityFrameworkCore                 ✅ Merged dev — not yet released
MicroKit.Messaging.MediatR                             ✅ Merged dev — not yet released (fix/messaging/mediatr in progress)
MicroKit.Messaging.Testing                             📋 Planned
MicroKit.Messaging.RabbitMQ                            ⏳ v2
MicroKit.Messaging.AzureServiceBus                     ⏳ v2
MicroKit.Messaging.Kafka                               ⏳ v2
```

---

## Sessions

Read the most recent file in `.claude-context/sessions/` before starting any work.

---

## MicroKit immutable flow (agents)

```
PRE-CODE  : implementer /plan → architect review → implementation
POST-CODE : distributed-context-specialist (if AsyncLocal / propagation)
            dependency-guardian (if .csproj modified)
            api-reviewer (if public API changed)
            → in separate Claude Code sessions
            → "Do not commit anything" mandatory in all post-code prompts
MERGE     : only after all relevant agents approved
/compact  : after full package implementation, before new session
```
