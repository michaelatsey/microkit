# MicroKit.Multitenancy — Module Brain

## 🎯 Purpose

MicroKit.Multitenancy is an **opinionated multitenancy layer** for .NET 10+ distributed architectures.
It provides host-agnostic tenant context propagation via `AsyncLocal`, a composable multi-strategy
resolution pipeline, EF Core data isolation, and Roslyn analyzers that enforce tenant boundaries at
compile time.

> **Differentiator vs Finbuckle.MultiTenant:** propagates tenant context outside HTTP (queues, jobs,
> gRPC, WebSockets) via a host-agnostic `AsyncLocal` context — not bound to `IHttpContextAccessor`.

```
MicroKit.Result              ← error modelling
MicroKit.Persistence         ← EF Core base (isolation bridge)
    └── MicroKit.Multitenancy ← tenant context, resolution pipeline, EF isolation
            └── Your app      ← handlers, resolvers, stores, EF configs
```

---

## 🗺️ Navigation

Always load the relevant file before working on a specific concern:

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule | `implementer` — plan before code |
| Architecture / contract decision | `.claude/rules/architecture.md` + `.claude-context/context/architectural-decisions.md` | `architect` |
| EF Core isolation concern | `.claude/rules/tenant-isolation.md` + `.claude-context/standards/ef-core-tenant-isolation.md` | `tenant-isolation-guardian` |
| Async context / propagation concern | `.claude/rules/async-context.md` + `.claude-context/standards/tenant-context-contracts.md` | `distributed-context-specialist` |
| Resolution strategy | `.claude/rules/resolution-pipeline.md` + `.claude-context/standards/resolution-strategy-contracts.md` | `architect` |
| Adding a resolver strategy | `.claude/workflows/adding-resolver.md` | `implementer` |
| Public API change | `.claude/rules/abstractions.md` + `.claude/rules/naming.md` | `api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/dependencies.md` + `.claude-context/context/dependency-graph.md` | `dependency-guardian` |
| Analyzer concern | `.claude/rules/analyzers.md` | `implementer` → `api-reviewer` |
| Release | `.claude/workflows/releasing-module.md` + `/release` | `release-manager` |

---

## 🏛️ Module Structure (5 packages — Phase 1)

```
MicroKit.Multitenancy/
├── src/
│   ├── MicroKit.Multitenancy.Abstractions/    ← ITenantContext, ITenantContextAccessor,
│   │                                             ITenantInfo, ITenantResolver,
│   │                                             ITenantResolutionStrategy, ITenantStore,
│   │                                             ITenantProvisioner, TenantProvisioningRequest,
│   │                                             TenantProvisionedEvent, TenantId (VO)
│   ├── MicroKit.Multitenancy/                 ← AsyncLocal context host-agnostic,
│   │                                             pipeline de résolution multi-stratégie,
│   │                                             store in-memory/config, DI registration
│   ├── MicroKit.Multitenancy.AspNetCore/      ← middleware + stratégies HTTP
│   │                                             (header, route, subdomain, claim, host)
│   ├── MicroKit.Multitenancy.EntityFrameworkCore/ ← query filter global TenantId,
│   │                                               interceptor SaveChanges stamp,
│   │                                               modes shared/schema/database
│   └── MicroKit.Multitenancy.Analyzers/       ← MKT001, MKT002, MKT003 Roslyn diagnostics
├── tests/
│   ├── MicroKit.Multitenancy.UnitTests/
│   ├── MicroKit.Multitenancy.IntegrationTests/
│   ├── MicroKit.Multitenancy.ArchitectureTests/
│   └── MicroKit.Multitenancy.PerformanceTests/
├── benchmarks/
└── samples/
```

---

## 📦 Dependency Graph

```
MicroKit.Multitenancy.Abstractions  ← MicroKit.Result
        ↑
MicroKit.Multitenancy (core)        ← Abstractions + Microsoft.Extensions.DependencyInjection.Abstractions
        ↑
MicroKit.Multitenancy.AspNetCore    ← Core + Microsoft.AspNetCore.App (FrameworkReference)
MicroKit.Multitenancy.EntityFrameworkCore ← Core + MicroKit.Persistence.Abstractions
                                               + MicroKit.Persistence.EntityFrameworkCore
                                               + Microsoft.EntityFrameworkCore
MicroKit.Multitenancy.Analyzers     ← Microsoft.CodeAnalysis.CSharp (netstandard2.0, build-time only)
```

**Cross-module:** MicroKit.Multitenancy is a **Level 3** module.

---

## 🔑 Key Contracts (quick reference)

### Tenant Identity
```csharp
TenantId                         // sealed record VO — wraps Guid
ITenantInfo                      // Id, Name, ConnectionString?, SchemaName?, IsActive
ITenantContext                   // CurrentTenant: ITenantInfo?
ITenantContextAccessor           // Get/SetTenant — backed by AsyncLocal
```

### Resolution Pipeline
```csharp
ITenantResolutionStrategy        // TryResolveAsync — returns Result<TenantId>
ITenantResolver                  // ResolveAsync — orchestrates strategies, returns Result<ITenantInfo>
```

### Store & Provisioning
```csharp
ITenantStore                     // FindAsync, ListAllAsync — tenant registry
ITenantProvisioner               // ProvisionAsync(TenantProvisioningRequest) → Result<TenantId>
TenantProvisioningRequest        // sealed record — Name, ConnectionString?, SchemaName?
TenantProvisionedEvent           // sealed record domain event
```

### EF Core isolation
```csharp
ITenantEntity                    // marker — TenantId property
ITenantDbContext                 // CurrentTenantId on DbContext
MultitenancyDbContextOptionsExtensions // .UseMultitenancy(ctx)
```

---

## 📐 Non-Negotiable Rules

1. **`ITenantContextAccessor` NEVER injected in a singleton** — always scoped/transient (MKT003)
2. **`ITenantEntity` implies `TenantId` is required** — never nullable on tenant-scoped entities (MKT001)
3. **`IgnoreQueryFilters()` needs explicit justification comment** — analyzers warn (MKT002)
4. **Resolution pipeline never throws** — returns `Result<T>.Failure` on unresolved tenant
5. **`AsyncLocal` context MUST be captured+restored** in continuations — never leaks between requests
6. **`sealed record` for VO/events** | **`sealed class` for services/handlers/strategies**
7. **`ValueTask<T>`** for all async methods | **`ConfigureAwait(false)`** throughout
8. **`CancellationToken ct = default`** always last
9. **Shouldly + NSubstitute** for tests — **FluentAssertions is banned**
10. **No inline `Version=`** on `PackageReference` — CPM via `Directory.Packages.props`

---

## 🤖 Available Agents

| Agent | Model | Trigger |
|-------|-------|---------|
| `implementer` | Opus | **First agent to invoke** before writing new code |
| `architect` | Opus | Contract decisions, module boundary changes, resolution pipeline design |
| `tenant-isolation-guardian` | Opus | EF Core query filters, interceptors, cross-tenant leak detection |
| `distributed-context-specialist` | Opus | AsyncLocal propagation, async context capture/restore, distributed scenarios |
| `api-reviewer` | Opus | Public API surface in Abstractions or Core — required before merge |
| `dependency-guardian` | Haiku | Any `.csproj` / project-reference change — fast PASS/BLOCK |
| `release-manager` | Sonnet | `/release` — 5-package release lifecycle |

---

## ⚡ Available Commands

| Command | Purpose |
|---------|---------|
| `/new-tenant-resolver` | Scaffold a custom `ITenantResolutionStrategy` implementation |
| `/new-tenant-store` | Scaffold a custom `ITenantStore` implementation |
| `/audit-tenant-isolation` | Detect EF query filter bypasses and cross-tenant leaks |
| `/review-architecture` | Run the architect agent against the module |

---

## 🔗 Context Layer

```
.claude-context/
├── standards/    ← tenant context contracts, resolution pipeline, EF isolation patterns
├── templates/    ← code-generation templates (resolver, store)
└── context/      ← ADRs, dependency graph
```

---

## 🔢 Versioning

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/multitenancy-v\\d+\\.\\d+"
  ]
}
```

Git tag convention: `multitenancy-v1.0.0`, `multitenancy-v1.1.0-beta.1`.
All 5 packages share one version per release.
