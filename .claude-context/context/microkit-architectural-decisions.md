# MicroKit — Architectural Decisions

This document records significant architectural decisions made for MicroKit modules.
Each ADR is immutable once merged; superseded decisions reference the ADR that replaces them.

---

## ADR-GLOBAL-001: ICurrentUserAccessor temporary duplication — MicroKit.MediatR.Abstractions vs MicroKit.Auth.Abstractions

**Date:** 2026-06-07
**Status:** Accepted — temporary
**Decided by:** Ange-Michaël Atsé
**Phase:** Cross-module

### Context

`ICurrentUserAccessor` was declared in `MicroKit.MediatR.Abstractions` as a pragmatic v1 placement
(ADR-008 in MicroKit.MediatR). ADR-008 explicitly documented the trigger condition for promotion:

> "A second module (e.g., MicroKit.Auth) needs to reference ICurrentUserAccessor and would
> otherwise take a dependency on MicroKit.MediatR.Abstractions purely for this interface."

During MicroKit.Auth.Abstractions implementation, `ICurrentUserAccessor` was declared independently
in `MicroKit.Auth.Abstractions`. The trigger condition fired.

### Why not resolve immediately

The correct resolution is a new `MicroKit.Abstractions` package at Level 0. This requires a full
bootstrap (new project, NuGet ID, CI step, versioning). The cost exceeds the benefit while
MicroKit.Auth Phase 1 is still in progress and has zero external consumers.

Making `MicroKit.Auth.Abstractions` depend on `MicroKit.MediatR.Abstractions` is forbidden —
Auth is Level 1, MediatR is Level 2; the edge Auth → MediatR violates the dependency graph.

### Decision

Accept temporary duplication. Both modules declare their own `ICurrentUserAccessor` until
`MicroKit.Abstractions` is bootstrapped.

### Trigger for resolution

Bootstrap `MicroKit.Abstractions` when any of the following is true:
- A third module needs `ICurrentUserAccessor`
- Any other cross-cutting primitive spans 2+ modules
- MicroKit.Auth Phase 1 is complete and stable

### Migration path

1. Create `MicroKit.Abstractions` (Level 0) — no MicroKit dependencies
2. Move `ICurrentUserAccessor` into `MicroKit.Abstractions`
3. Add `MicroKit.Abstractions` dependency to `MicroKit.MediatR.Abstractions` and `MicroKit.Auth.Abstractions`
4. Provide deprecation shims in both for one release cycle
5. Update dependency graph in root `CLAUDE-MICROKIT.md`

### Related ADRs

- `modules/MicroKit.MediatR/.claude-context/context/architectural-decisions.md` — ADR-008
- `modules/MicroKit.Auth/.claude-context/context/microkit-auth-architectural-decisions.md` — ADR-AUTH-003
