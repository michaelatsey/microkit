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
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule file | `logging-implementer` — plan before code |
| Architecture decision | `.claude/rules/logging-architecture.md` + `.claude-context/context/logging-architectural-decisions.md` | `logging-architect` |
| Adding an enricher | `.claude/workflows/logging-adding-enricher.md` + `/logging-new-enricher` command | `logging-implementer` → `logging-performance-reviewer` |
| Adding a provider | `.claude/workflows/logging-adding-provider.md` + `/logging-new-provider` command | `logging-implementer` → `logging-dependency-guardian` |
| Adding a Roslyn analyzer | `.claude/workflows/logging-creating-analyzer.md` + `/logging-new-analyzer` command | `logging-implementer` → `logging-analyzer-reviewer` |
| Performance concern | `.claude/rules/logging-performance.md` + `.claude/skills/logging-profiling/SKILL.md` | `logging-performance-reviewer` |
| OpenTelemetry work | `.claude/rules/logging-opentelemetry.md` + `.claude/skills/logging-opentelemetry/SKILL.md` | `logging-observability-reviewer` |
| Public API change | `.claude/rules/logging-abstractions.md` + `.claude/rules/logging-naming.md` | `logging-api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/logging-dependencies.md` + `.claude-context/context/logging-dependency-graph.md` | `logging-dependency-guardian` — auto on `.csproj` edit |
| Release | `.claude/workflows/logging-releasing-module.md` + `/logging-release` command | `logging-release-manager` |
| Naming a property/event | `.claude-context/standards/log-properties.md` + `.claude-context/standards/logging-event-ids.md` | `logging-api-reviewer` if Abstractions touched |

---

## 🏛️ Module Structure

```
MicroKit.Logging/
├── src/
│   ├── MicroKit.Logging.Abstractions/     ← pure contracts, MEL.Abstractions only
│   ├── MicroKit.Logging/                  ← enrichment pipeline, context propagation
│   ├── MicroKit.Logging.OpenTelemetry/    ← OTEL bridge (optional)
│   ├── MicroKit.Logging.Serilog/          ← Serilog integration (planned v2)
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
        └── MicroKit.Logging.Serilog  (planned v2 — not yet implemented)
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
| `logging-implementer` | Opus | **First agent to invoke** before writing any new code — produces a plan and waits for approval. active plan mode when triggered |
| `logging-architect` | Opus | Architecture decisions, new abstractions, dependency graph changes |
| `logging-api-reviewer` | Opus | Public API surface changes in Abstractions or Core — required before merge |
| `logging-performance-reviewer` | Sonnet | Any hot-path code, enrichment pipeline changes, benchmark deltas |
| `logging-observability-reviewer` | Sonnet | OTEL bridge, ActivitySource, DiagnosticSource, tracing |
| `logging-analyzer-reviewer` | Sonnet | Roslyn analyzer rules, diagnostics IDs, code fixes |
| `logging-release-manager` | Sonnet | `/logging-release` command, NuGet packaging, tag preparation |
| `logging-dependency-guardian` | Haiku | Any `<PackageReference>` or project reference change — fast PASS/BLOCK |

---

## ⚡ Available Commands

| Command | Purpose |
|---------|---------|
| `/logging-new-provider` | Scaffold a new logging provider integration |
| `/logging-new-enricher` | Scaffold a new `ILogEnricher` implementation |
| `/logging-new-analyzer` | Scaffold a new Roslyn analyzer + code fix |
| `/logging-new-generator` | Scaffold a new source generator |
| `/logging-review-architecture` | Run architecture review agent |
| `/logging-review-performance` | Run performance review agent |
| `/logging-review-observability` | Run observability review agent |
| `/logging-generate-tests` | Generate test suite for a target class |
| `/logging-generate-benchmarks` | Generate BenchmarkDotNet suite |
| `/logging-release` | Prepare and validate release |

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
