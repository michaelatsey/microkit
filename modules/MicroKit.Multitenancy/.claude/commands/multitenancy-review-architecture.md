# Command: /multitenancy-review-architecture

Run the multitenancy-architect agent against MicroKit.Multitenancy for a full architecture review.

## Usage
```
/multitenancy-review-architecture
```

## What gets reviewed

1. **Abstractions minimality** — only what a consuming module needs to compile
2. **Dependency graph** — no forbidden cross-module refs, CPM compliant
3. **Resolution pipeline** — strategies return Result<T>, never throw
4. **AsyncLocal correctness** — Scoped registration, capture/restore pattern
5. **EF Core isolation** — query filter completeness, interceptor registration
6. **Analyzer coverage** — MKT001/MKT002/MKT003 diagnostics complete

## Agent invocation

Invokes the `architect` agent with context:
- `.claude/CLAUDE.md`
- `.claude/rules/multitenancy-architecture.md`
- `.claude/rules/multitenancy-abstractions.md`
- `.claude/rules/multitenancy-dependencies.md`
- `.claude/rules/multitenancy-tenant-isolation.md`
- `.claude/rules/multitenancy-async-context.md`
- `.claude/rules/multitenancy-resolution-pipeline.md`
- `.claude-context/context/multitenancy-architectural-decisions.md`
- `.claude-context/context/multitenancy-dependency-graph.md`
