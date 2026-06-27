# Command: /tenancy-review-architecture

Run the tenancy-architect agent against MicroKit.Tenancy for a full architecture review.

## Usage
```
/tenancy-review-architecture
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
- `.claude/rules/tenancy-architecture.md`
- `.claude/rules/tenancy-abstractions.md`
- `.claude/rules/tenancy-dependencies.md`
- `.claude/rules/tenancy-tenant-isolation.md`
- `.claude/rules/tenancy-async-context.md`
- `.claude/rules/tenancy-resolution-pipeline.md`
- `.claude-context/context/tenancy-architectural-decisions.md`
- `.claude-context/context/tenancy-dependency-graph.md`
