# MicroKit.Logging — Module Brain

## 🎯 Purpose

MicroKit.Logging is **not a logging library**. It is the **observability platform** of the entire MicroKit ecosystem.

Its mission: provide correlation, enrichment standardization, and distributed context propagation — without coupling consumers to a specific sink or provider.

> **Core principle:** other MicroKit modules depend on `MicroKit.Logging.Abstractions` only. Never on the concrete core.

---

## 🗺️ Navigation

Always load the relevant file before working on a specific concern:

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule file | `implementer` — plan before code |
| Architecture decision | `.claude/rules/architecture.md` + `.claude-context/context/architectural-decisions.md` | `architect` |
| Adding an enricher | `.claude/workflows/adding-enricher.md` + `/new-enricher` command | `implementer` → `performance-reviewer` |
| Adding a provider | `.claude/workflows/adding-provider.md` + `/new-provider` command | `implementer` → `dependency-guardian` |
| Adding a Roslyn analyzer | `.claude/workflows/creating-analyzer.md` + `/new-analyzer` command | `implementer` → `analyzer-reviewer` |
| Performance concern | `.claude/rules/performance.md` + `.claude/skills/profiling/SKILL.md` | `performance-reviewer` |
| OpenTelemetry work | `.claude/rules/opentelemetry.md` + `.claude/skills/opentelemetry/SKILL.md` | `observability-reviewer` |
| Public API change | `.claude/rules/abstractions.md` + `.claude/rules/naming.md` | `api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/dependencies.md` + `.claude-context/context/dependency-graph.md` | `dependency-guardian` — auto on `.csproj` edit |
| Release | `.claude/workflows/releasing-module.md` + `/release` command | `release-manager` |
| Naming a property/event | `.claude-context/standards/log-properties.md` + `.claude-context/standards/event-ids.md` | `api-reviewer` if Abstractions touched |

---

## 🏛️ Module Structure

```
MicroKit.Logging/
├── src/
│   ├── MicroKit.Logging.Abstractions/     ← pure contracts, MEL.Abstractions only
│   ├── MicroKit.Logging/                  ← enrichment pipeline, context propagation
│   ├── MicroKit.Logging.OpenTelemetry/    ← OTEL bridge (optional)
│   ├── MicroKit.Logging.Serilog/          ← Serilog integration (optional)
│   ├── MicroKit.Logging.AspNetCore/       ← HTTP middleware, request enrichment
│   ├── MicroKit.Logging.Diagnostics/      ← ActivitySource, DiagnosticSource
│   ├── MicroKit.Logging.Analyzers/        ← Roslyn analyzers, DX enforcement
│   └── MicroKit.Logging.Generators/       ← Source generators for LoggerMessage
├── tests/
│   ├── MicroKit.Logging.UnitTests/
│   ├── MicroKit.Logging.IntegrationTests/
│   ├── MicroKit.Logging.ArchitectureTests/
│   └── MicroKit.Logging.PerformanceTests/
├── benchmarks/
└── samples/
    ├── AspNetCore/
    ├── OpenTelemetry/
    ├── Serilog/
    ├── CQRS/
    └── DistributedTracing/
```

---

## 📦 Dependency Graph

```
MicroKit.Logging.Abstractions        ← MEL.Abstractions only
        ↑
MicroKit.Logging                     ← core pipeline
        ├── MicroKit.Logging.AspNetCore
        ├── MicroKit.Logging.Diagnostics
        ├── MicroKit.Logging.OpenTelemetry
        └── MicroKit.Logging.Serilog
```

**Other MicroKit modules:** depend on `MicroKit.Logging.Abstractions` only.

---

## 📐 Non-Negotiable Rules

1. **No `IMicroKitLogger`** — never wrap `ILogger<T>`
2. **No concrete dependency** from other modules — `Abstractions` only
3. **No runtime reflection** — use source generators or cached delegates
4. **No `AsyncLocal` abuse** — scoped, intentional, documented
5. **No circular dependencies** — enforce via ArchitectureTests
6. **`LoggerMessage` everywhere** on hot paths — zero allocation
7. **`ValueTask<T>` + `ConfigureAwait(false)`** in all async paths
8. **`CancellationToken ct = default`** always last parameter
9. **Canonical property names only** — see `.claude-context/standards/log-properties.md`
10. **`sealed`** on all records, handlers, enrichers

---

## 🏷️ Canonical Log Properties

| Property | Type | Source |
|----------|------|--------|
| `CorrelationId` | `string` | Propagated across boundaries |
| `TraceId` | `string` | W3C TraceContext / Activity |
| `SpanId` | `string` | W3C TraceContext / Activity |
| `TenantId` | `string` | MicroKit.MultiTenancy |
| `UserId` | `string` | MicroKit.Auth |
| `RequestId` | `string` | HTTP / message |
| `OperationId` | `string` | Business operation scope |
| `CommandName` | `string` | MicroKit.MediatR |
| `MessageId` | `string` | MicroKit.Messaging |

> **Full reference:** `.claude-context/standards/log-properties.md`

---

## 🤖 Available Agents

| Agent | Model | Trigger |
|-------|-------|---------|
| `implementer` | Opus | **First agent to invoke** before writing any new code — produces a plan and waits for approval. active plan mode when triggered |
| `architect` | Opus | Architecture decisions, new abstractions, dependency graph changes |
| `api-reviewer` | Opus | Public API surface changes in Abstractions or Core — required before merge |
| `performance-reviewer` | Sonnet | Any hot-path code, enrichment pipeline changes, benchmark deltas |
| `observability-reviewer` | Sonnet | OTEL bridge, ActivitySource, DiagnosticSource, tracing |
| `analyzer-reviewer` | Sonnet | Roslyn analyzer rules, diagnostics IDs, code fixes |
| `release-manager` | Sonnet | `/release` command, NuGet packaging, tag preparation |
| `dependency-guardian` | Haiku | Any `<PackageReference>` or project reference change — fast PASS/BLOCK |

---

## ⚡ Available Commands

| Command | Purpose |
|---------|---------|
| `/new-provider` | Scaffold a new logging provider integration |
| `/new-enricher` | Scaffold a new `ILogEnricher` implementation |
| `/new-analyzer` | Scaffold a new Roslyn analyzer + code fix |
| `/new-generator` | Scaffold a new source generator |
| `/review-architecture` | Run architecture review agent |
| `/review-performance` | Run performance review agent |
| `/review-observability` | Run observability review agent |
| `/generate-tests` | Generate test suite for a target class |
| `/generate-benchmarks` | Generate BenchmarkDotNet suite |
| `/release` | Prepare and validate release |

---

## 🔗 Context Layer

Extended intelligence (standards, templates, ADRs) lives in `.claude-context/`:

```
.claude-context/
├── standards/          ← canonical values (property names, event IDs, categories)
├── templates/          ← code generation templates
└── context/            ← ADRs, ecosystem overview, dependency graph
```

These are **not** Claude Code runtime files. They are loaded explicitly by agents and commands when needed.

---

## 🔢 Versioning

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/logging-v\\d+\\.\\d+"
  ]
}
```

Git tag convention: `logging-v1.0.0`, `logging-v1.1.0-beta.1`
