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
| **MicroKit.Result** | `modules/MicroKit.Result/` | `modules/MicroKit.Result/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Domain** | `modules/MicroKit.Domain/` | `modules/MicroKit.Domain/.claude/` | ✅ Released 1.0.0-preview.4 |
| **MicroKit.Logging** | `modules/MicroKit.Logging/` | `modules/MicroKit.Logging/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.MediatR** | `modules/MicroKit.MediatR/` | `modules/MicroKit.MediatR/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Persistence** | `modules/MicroKit.Persistence/` | `modules/MicroKit.Persistence/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Multitenancy** | `modules/MicroKit.Multitenancy/` | `modules/MicroKit.Multitenancy/.claude/` | ✅ Released 1.0.0-preview.1 |
| **MicroKit.Auth** | `modules/MicroKit.Auth/` | `modules/MicroKit.Auth/.claude/` | 🚧 In progress — Abstractions ✅ Core ✅ AspNetCore ✅ Permissions ✅ merged dev |
| **MicroKit.Messaging** | `modules/MicroKit.Messaging/` | `modules/MicroKit.Messaging/.claude/` | 📋 Planned |
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
│   │   └── release-*.yml             ← per-module release
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
MicroKit.Domain          ← no dependency on other modules
MicroKit.Result          ← no dependency on other modules
MicroKit.Logging         ← ADR-006: does NOT depend on Result (permanent)
MicroKit.Observability   ← may depend on Result, Logging
MicroKit.Auth            ← may depend on Result, Domain
MicroKit.Caching         ← may depend on Result
MicroKit.Persistence     ← may depend on Result, Domain
MicroKit.Messaging       ← may depend on Result, Domain, Persistence (outbox)
MicroKit.Http            ← may depend on Result, Observability
MicroKit.MediatR         ← may depend on Result, Domain, Logging.Abstractions
MicroKit.Multitenancy    ← may depend on Result, Auth, Persistence
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
result-v1.0.0-preview.1        → MicroKit.Result release
domain-v1.0.0-preview.1        → MicroKit.Domain release
logging-v1.0.0-preview.1       → MicroKit.Logging release
mediatr-v1.0.0-preview.1       → MicroKit.MediatR release
persistence-v1.0.0-preview.1   → MicroKit.Persistence release
multitenancy-v1.0.0-preview.1  → MicroKit.Multitenancy release
auth-v1.0.0-preview.1          → MicroKit.Auth release
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

- `sealed record` for errors/VOs/events | `sealed class` for handlers/behaviors
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
MicroKit.Result                                        ✅ 1.0.0-preview.1
MicroKit.Result.AspNetCore                             ✅ 1.0.0-preview.1
MicroKit.Domain                                        ✅ 1.0.0-preview.4
MicroKit.Logging                                       ✅ 1.0.0-preview.1
MicroKit.Logging.Abstractions                          ✅ 1.0.0-preview.1
MicroKit.Logging.OpenTelemetry                         ✅ 1.0.0-preview.1
MicroKit.Logging.AspNetCore                            ✅ 1.0.0-preview.1
MicroKit.Logging.Diagnostics                           ✅ 1.0.0-preview.1
MicroKit.Logging.Analyzers                             ✅ 1.0.0-preview.1
MicroKit.Logging.Generators                            ✅ 1.0.0-preview.1
MicroKit.MediatR                                       ✅ 1.0.0-preview.1
MicroKit.MediatR.Abstractions                          ✅ 1.0.0-preview.1
MicroKit.MediatR.Behaviors                             ✅ 1.0.0-preview.1
MicroKit.MediatR.Testing                               ✅ 1.0.0-preview.1
MicroKit.Persistence.Abstractions                      ✅ 1.0.0-preview.1
MicroKit.Persistence                                   ✅ 1.0.0-preview.1
MicroKit.Persistence.EntityFrameworkCore               ✅ 1.0.0-preview.1
MicroKit.Persistence.EntityFrameworkCore.PostgreSql    ✅ 1.0.0-preview.1
MicroKit.Persistence.EntityFrameworkCore.SqlServer     ✅ 1.0.0-preview.1
MicroKit.Persistence.Specifications                    ✅ 1.0.0-preview.1
MicroKit.Persistence.Testing                           ✅ 1.0.0-preview.1
MicroKit.Persistence.Analyzers                         ✅ 1.0.0-preview.1
MicroKit.Multitenancy.Abstractions                     ✅ 1.0.0-preview.1
MicroKit.Multitenancy                                  ✅ 1.0.0-preview.1
MicroKit.Multitenancy.AspNetCore                       ✅ 1.0.0-preview.1
MicroKit.Multitenancy.EntityFrameworkCore              ✅ 1.0.0-preview.1
MicroKit.Multitenancy.Analyzers                        ✅ 1.0.0-preview.1
MicroKit.Auth.Abstractions                             🚧 In progress (merged dev)
MicroKit.Auth                                          🚧 In progress (merged dev)
MicroKit.Auth.AspNetCore                               🚧 In progress (merged dev)
MicroKit.Auth.Permissions                              🚧 In progress (merged dev)
MicroKit.Auth.Roles                                    📋 Planned Phase 1
MicroKit.Auth.Jwt                                      📋 Planned Phase 1
MicroKit.Auth.Supabase                                 📋 Planned Phase 1
MicroKit.Auth.Multitenancy                             📋 Planned Phase 1
MicroKit.Auth.Testing                                  📋 Planned Phase 1
MicroKit.Messaging                                     📋 Planned
MicroKit.Messaging.AzureServiceBus                     📋 Planned
MicroKit.Messaging.RabbitMQ                            📋 Planned
```

## Sessions

Read the most recent file in `.claude-context/sessions/` before starting any work.
