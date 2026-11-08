# MicroKit.Persistence — Module Brain

## 🎯 Purpose

MicroKit.Persistence is an **opinionated persistence layer** for .NET 10+. It wraps Entity Framework
Core (and future providers) behind clean repository abstractions, wires the Unit of Work pattern into
the MediatR pipeline via `ITransactionalContext`, and enforces CQRS-aligned data-access patterns at
build time via Roslyn analyzers.

> **Core principle:** the domain layer defines what entities are persisted; this module defines how.
> Handlers write to aggregates via `IRepository<TAggregate, TId>`; handlers read via `IReadRepository<T>`.
> The `IUnitOfWork` — moved here from `MicroKit.Domain` (ADR-001) — lives at the persistence boundary
> because committing is an infrastructure concern, not a domain concern.

```
MicroKit.Domain              ← defines IAggregateRoot, Specification<T>
    └── MicroKit.Persistence ← IRepository, IUnitOfWork, QueryOptions, EF Core bridge
            └── Your app     ← clean command/query handlers with typed repos
```

---

> **Issue #196 is being delivered on this branch, one commit per phase.** Where this brain and the
> code disagree, the code is authoritative; lines marked "#196 phase N" describe a change that phase
> makes. Each phase updates these files as its code lands.

## 🗺️ Navigation

Always load the relevant file before working on a specific concern:

| Task | Load first | Agent |
|------|-----------|-------|
| **Implementing anything new** | `.claude/CLAUDE.md` + relevant rule file | `implementer` — plan before code |
| Architecture / pattern decision | `.claude/rules/architecture.md` + `.claude-context/context/architectural-decisions.md` | `architect` |
| Adding a repository | `.claude/workflows/adding-repository.md` | `implementer` → `test-generator` |
| Adding a provider (PostgreSQL/SQL Server) | `.claude/workflows/adding-provider.md` | `implementer` → `ef-core-specialist` |
| Adding a specification | `.claude/workflows/adding-specification.md` + `.claude/rules/specifications.md` | `implementer` |
| EF Core concern (mapping, migration, query) | `.claude/rules/ef-core-patterns.md` + `.claude/skills/ef-core-patterns/SKILL.md` | `ef-core-specialist` |
| Performance concern | `.claude/rules/performance.md` + `.claude/skills/benchmarking/SKILL.md` | `performance-reviewer` |
| Public API change | `.claude/rules/abstractions.md` + `.claude/rules/naming.md` | `api-reviewer` — required before merge |
| Dependency / `.csproj` change | `.claude/rules/dependencies.md` + `.claude-context/context/dependency-graph.md` | `dependency-guardian` — after every `.csproj` change |
| Generating tests | `.claude/rules/testing.md` | `test-generator` |
| Transaction behavior integration | `.claude-context/context/transaction-behavior-integration.md` | `architect` + `ef-core-specialist` |
| Release | `.claude/workflows/releasing-module.md` + `/release` | `release-manager` |

---

## 🏛️ Module Structure (8 projects)

```
MicroKit.Persistence/
├── src/
│   ├── MicroKit.Persistence.Abstractions/        ← IRepository<TAggregate, TId>, IUnitOfWork,
│   │                                                ITransactionalContext, IPagedResult<T>,
│   │                                                PersistenceException
│   ├── MicroKit.Persistence/                      ← ISpecificationEvaluator, QueryOptions,
│   │                                                pagination, transaction pipeline, conventions
│   ├── MicroKit.Persistence.EntityFrameworkCore/  ← EfRepository, EfUnitOfWork,
│   │                                                EfSpecificationEvaluator, ITransactionalUnitOfWork
│   ├── MicroKit.Persistence.EntityFrameworkCore.PostgreSql/  ← Npgsql provider
│   ├── MicroKit.Persistence.EntityFrameworkCore.SqlServer/   ← SqlServer provider
│   ├── MicroKit.Persistence.Specifications/       ← QueryOptions extensions, spec helpers
│   ├── MicroKit.Persistence.Testing/              ← InMemoryRepository, test helpers
│   └── MicroKit.Persistence.Analyzers/            ← Roslyn analyzers (build-time only)
├── tests/
│   ├── MicroKit.Persistence.UnitTests/
│   ├── MicroKit.Persistence.IntegrationTests/
│   ├── MicroKit.Persistence.ArchitectureTests/
│   └── MicroKit.Persistence.PerformanceTests/
├── benchmarks/
└── samples/
```

---

## 📦 Dependency Graph

```
MicroKit.Persistence.Abstractions  ← MicroKit.Domain
        ↑
MicroKit.Persistence (core)        ← Abstractions
        ↑
MicroKit.Persistence.EntityFrameworkCore  ← Core + Microsoft.EntityFrameworkCore
        ↑
MicroKit.Persistence.EntityFrameworkCore.PostgreSql  ← EntityFrameworkCore + Npgsql.EF
MicroKit.Persistence.EntityFrameworkCore.SqlServer   ← EntityFrameworkCore + SqlServer provider
MicroKit.Persistence.Specifications  ← Core
MicroKit.Persistence.Testing          ← Core + NSubstitute
MicroKit.Persistence.Analyzers        ← Microsoft.CodeAnalysis.CSharp (build-time only)
```

**Cross-module:** MicroKit.Persistence is a **Level 2** module — may depend on Level 0
(`MicroKit.Domain`). Intra-module references are unconditional `ProjectReference` (ADR-GLOBAL-002 D5). Full graph: `.claude-context/context/dependency-graph.md`.

---

## 🔑 Key Contracts (quick reference)

### Write side
```csharp
IRepository<TAggregate, TId>  // FindAsync(TId) — whole aggregate, tracked — AddAsync, DeleteAsync; never commits
IUnitOfWork                   // CommitAsync(CancellationToken), DiscardChanges() — source-compatible, frozen
ITransactionalContext         // ExecuteAsync<TState>(…) / ExecuteAsync<TState, TResult>(…) — frozen
```

### Read side
```csharp
IReadRepository<TAggregate>   // FindAsync, ListAsync, AnyAsync, CountAsync
IPagedResult<T>               // Items, TotalCount, Page, PageSize
QueryOptions<TAggregate>      // Specification + include strategy + pagination
```

### Composite (EF Core)
```csharp
ITransactionalUnitOfWork : IUnitOfWork, ITransactionalContext
// EfUnitOfWork implements ITransactionalUnitOfWork
// Registered as scoped; handlers inject IUnitOfWork + ITransactionalContext
```

### Transaction pipeline integration
```csharp
// TransactionBehavior in MicroKit.MediatR.Behaviors depends on ITransactionalContext
// from this module (Persistence.Abstractions). The behavior wraps ICommand handlers only.
// See .claude-context/context/transaction-behavior-integration.md
```

---

## 🔄 Specification Pattern

`Specification<T>` (with `Criteria` only — no `Include` or pagination) lives in **MicroKit.Domain**.
It is a domain-level concept: pure predicate, no infrastructure concern.

`QueryOptions<TAggregate>` (in `MicroKit.Persistence` core) wraps a specification and adds the
**loading strategy** (eager includes, split queries, AsNoTracking) and **pagination**. This separates
WHAT to query from HOW to execute it. See ADR-002.

```csharp
// In domain
public sealed class ActiveUsersSpec : Specification<User>
{
    public ActiveUsersSpec() => AddCriteria(u => u.IsActive && !u.IsDeleted);
}

// In query handler
var opts = new QueryOptions<User>(new ActiveUsersSpec())
    .WithIncludes(q => q.Include(u => u.Roles))
    .WithPagination(page: 1, pageSize: 20)
    .AsNoTracking();

var result = await _repo.ListAsync(opts, ct);
```

---

## ⚠️ Breaking Change — IUnitOfWork Moved from Domain

> `IUnitOfWork` previously lived in `MicroKit.Domain`. It has been **moved to
> `MicroKit.Persistence.Abstractions`** in this module (ADR-001). Any consumer that
> depended on `MicroKit.Domain` for `IUnitOfWork` must update their `using` directive.
> `MicroKit.Domain` retains `IAggregateRoot`, `IDomainEvent`, and `Specification<T>`.
> See `.claude-context/context/architectural-decisions.md` ADR-001 for rationale and
> migration guidance.

---

## 📐 Non-Negotiable Rules

1. **`IUnitOfWork.CommitAsync()`** on the public interface — never expose `SaveChangesAsync` to consumers
2. **`IReadRepository`** never calls `CommitAsync` or mutates state — analyzers enforce this
3. **`AsNoTracking()`** on all read queries via `QueryOptions` — `IReadRepository` implementations must not track
4. **`Specification<T>` stays in Domain** — `QueryOptions` wraps it; the evaluator lives in Core
5. **EF Core stays out of Abstractions** — no `DbContext`, `IQueryable`, or EF types in `.Abstractions`
6. **`sealed class`** for repositories, evaluators, UoW; **`sealed record`** for QueryOptions, IPagedResult impls
7. **`ValueTask<T>`** for all repository methods — `ConfigureAwait(false)` everywhere in lib code
8. **`CancellationToken ct = default`** always last
9. **Shouldly + NSubstitute** for tests — **FluentAssertions is banned**
10. **No inline `Version=`** on `PackageReference` — CPM via `Directory.Packages.props`

---

## 🤖 Available Agents

| Agent | Model | Trigger |
|-------|-------|---------|
| `implementer` | Opus | **First agent to invoke** before writing new code — produces a plan and waits for approval |
| `architect` | Opus | Repository contracts, UoW design, pattern decisions, module boundary changes |
| `ef-core-specialist` | Opus | EF Core mapping, migrations, query optimization, N+1 detection, split queries |
| `api-reviewer` | Opus | Public API surface in Abstractions or core — required before merge |
| `performance-reviewer` | Sonnet | Query hot-path, allocations, tracking overhead, N+1 regressions |
| `dependency-guardian` | Haiku | Any `.csproj` / project-reference change — fast PASS/BLOCK |
| `release-manager` | Sonnet | `/release` — 8-package release lifecycle |
| `test-generator` | Sonnet | Generates Shouldly + NSubstitute suites |

---

## ⚡ Available Commands

This module defines no slash commands. The repository root provides `/release`,
`/audit-dependencies`, `/bootstrap-module-claude` and `/new-module`.

---

## 🔗 Context Layer

Extended intelligence (standards, templates, ADRs) lives in `.claude-context/`:

```
.claude-context/
├── standards/   ← canonical contracts (repository, transaction, query-options, EF conventions, perf budget)
├── templates/   ← code-generation templates (repository, read-repo, EF config, spec, test-repo)
└── context/     ← ADRs, dependency graph, ecosystem overview, transaction-behavior integration
```

These are **not** Claude Code runtime files. Agents and commands load them explicitly when needed.

---

## 🔢 Versioning

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/persistence-v\\d+\\.\\d+"
  ]
}
```

Git tag convention: `persistence-v1.0.0`, `persistence-v1.1.0-beta.1`. All 8 packages share one version.

## 🔗 References
- [EF Core Docs](https://learn.microsoft.com/en-us/ef/core/) · [Specification Pattern — Evans](https://www.martinfowler.com/apsupp/spec.pdf) · [Repository Pattern — Fowler](https://martinfowler.com/eaaCatalog/repository.html)
