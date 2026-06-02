# Command: /review-architecture

Run the architect agent against MicroKit.Multitenancy for a full architecture review.

## Usage
```
/review-architecture
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
- `.claude/rules/architecture.md`
- `.claude/rules/abstractions.md`
- `.claude/rules/dependencies.md`
- `.claude/rules/tenant-isolation.md`
- `.claude/rules/async-context.md`
- `.claude/rules/resolution-pipeline.md`
- `.claude-context/context/architectural-decisions.md`
- `.claude-context/context/dependency-graph.md`
