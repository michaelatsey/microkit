## ADR-GLOBAL-002: Release and versioning model — declared release set over a shared version

**Date:** 2026-09-05
**Status:** Accepted
**Decided by:** Ange-Michaël Atsé
**Phase:** Cross-module

### Context

The repository was believed to version each module independently through `version.json` and
Nerdbank.GitVersioning. A full audit of the mechanism (`versioning-audit.md`, 2026-09-05, at
`HEAD = 9e5c682`) established that **no such mechanism exists and never has**: NBGV is not
installed in the current tree nor in any of 287 reachable commits, the nine `version.json` files
are read by nothing, and every ordinary build stamps the MSBuild default `1.0.0`. The only live
version computation is a bash prefix-strip inside each release workflow, producing a
`PackageVersion` that reaches the nuspec and nothing else — `-p:AssemblyVersion` passed to
`pack --no-build` has never had any effect, proven by identical SHA256 of the DLL in `bin/` and
in the `.nupkg`.

This is not a drift to correct. There is no versioning system to correct. This ADR designs one.

Three further audit findings shape it:

1. **Publication scope is implicit.** Each release workflow packs a module *solution*, whose
   `/deps/` folder holds sibling-module projects needed for a source restore, so `pack` emits
   those siblings too and `push` publishes whichever do not already exist. Nine foreign
   publications resulted, all still listed; `--skip-duplicate` swallowed twelve duplicate pushes
   silently. This is a property of the mechanism, reproducible on every release, not an accident
   of one module.
2. **`CIReleaseBuild` conflates two responsibilities** — how a dependency resolves, and, through
   the solution's contents, what gets packed. The second is the defect.
3. **The catalogue holds several architectural eras** — 83 package ids, 232 id/version pairs, 118
   from a deleted repository-wide workflow, plus 19 stable `1.0.0` packages published by accident
   before `299b6dd4` added `-p:PackageVersion`.

### Decision

**D1 — A release is a declared set of packages.** An explicit event with a version, a commit, a
manifest of package ids, and a changelog. Not a module, not a solution. Membership in the build
graph confers no right to publish: a project is compiled because something needs it, a package is
published because the manifest names it. Neither fact derives from the other. This removes foreign
publications at the source; `--skip-duplicate` stops being the idempotency mechanism.

**D2 — One version per release, shared by every package in it.** `MicroKit 2.1.0` names a coherent
state of the ecosystem. A shared version does not mean republishing everything: packages outside
the set keep the version they have, and `2.0.1` may contain one package. Fully independent
per-package versioning was rejected — these packages are coupled by contracts and a dependency
graph, and independent numbers would impose a compatibility matrix on consumers.

**D3 — `2.0.0` is the boundary with the previous catalogue.** `1.0.0` is unavailable on 19 ids
including `MicroKit.Domain`, `MicroKit.Result` and the whole `MicroKit.Persistence` family;
unlisting does not free the number, since an unlisted version remains in the v3-flatcontainer index
used for resolution. `1.0.1` and `1.1.0` were available and rejected: the boundary must be legible,
and `1.0.1` would inscribe the new platform in the continuity of an accident. Package ids are kept,
history is not deleted, foreign and accidental publications are unlisted.

**D4 — The release set is closed under dependency.** The declared set is the input; the effective
set adds, transitively, every module a member depends on **and** whose source changed since the
previous release tag. Without closure a release can publish a package whose declared dependencies
do not compile together, undetected until a consumer restores. Accepted cost: the effective set is
computed, not hand-written.

**D5 — A module is a directory under `modules/`; ownership derives from the path.**
`modules/MicroKit.Persistence/` owns every id beginning with `MicroKit.Persistence`. Intra-module
references are always `ProjectReference`, in every mode including `CIReleaseBuild=true` — a module
releases as a unit and its internal parts never reach each other through NuGet. This retires a
shipped defect: six intra-module references sit inside `CIReleaseBuild` blocks in
`MicroKit.Persistence`, and `persistence-v1.0.0-preview.3` published seven packages whose manifests
depend on their own siblings at `preview.2`.

**D6 — The ownership map is derived, never declared.** No file lists which package belongs to which
module; the path is the map, and a CI guard asserts every packable project produces an id
consistent with its location. A hand-maintained map drifts — the audit found that failure mode in
`.claude/CLAUDE.md` on the versioning mechanism, the dependency graph, the workflow count and the
physical structure.

**D7 — Closure is computed on the `.csproj` graph; the diff is only the trigger.** "Changed" means
a path diff between the previous release tag and `HEAD`, restricted to `modules/<Module>/src/` —
tests, documentation, `.claude/` directories and module brains do not trigger a republication. This
definition is load-bearing and is a decision, not an implementation detail: too wide and a README
republishes the platform, too narrow and a real change ships without its dependents.

**D8 — Tags are `v<semver>`; pre-releases are `preview.N`.** One tag per release, no module prefix.
Pre-release iterations increment by one on each release regardless of the nature of the change. The
separator matters: SemVer 2 orders `preview.10` after `preview.9` because the numeric identifier
compares as a number, where `preview10` would compare as text. `alpha`/`beta`/`rc` were rejected as
promising a maturation process that does not exist here.

**D9 — The version lives in `eng/Versions.props`.** One file at the root declares `VersionPrefix`,
`PreReleaseLabel` and `PreReleaseIteration`; `Directory.Build.props` inherits from it and every
artefact is stamped from it — `PackageVersion`, `AssemblyVersion`, `FileVersion`,
`InformationalVersion`. The git tag is a release marker, not the source. Neither NBGV nor MinVer:
both derive from git, so a local build cannot state the truth and a version change is invisible in
a diff. A versioned file is reviewable in a PR; a tag is not. It is also the option with the fewest
moving parts, which matters here — nine `version.json` files lied for months precisely because
nothing made the lie visible. Arcade's `eng/Versions.props` is the model; Arcade itself is not
adopted, being an MSBuild SDK that replaces the build system and assumes Azure DevOps, the Build
Asset Registry, Darc and Maestro.

**D10 — `2.0.0` stable ships when the whole ecosystem is ready.** Not after a fixed number of
previews, and not module by module: there is no `MicroKit.Domain 2.0.0` stable while
`MicroKit.Messaging` is still in preview. Until then ADR-MSG-016 §4 holds — breaks ship outright on
`preview.*` packages with no external consumers. **No `[Obsolete]` member exists in MicroKit today
and none is to be added.** The judgement "the API is frozen" therefore also means "no known break
is still owed": known breaks ship before stable, where they cost nothing.

**D11 — Caching, Http and Observability are outside the publishable perimeter.** Three directories
holding a single 0-byte `README.md` each and no solution. They are planned modules, not late ones,
and do not block `2.0.0`. `MicroKit.Domain.Benchmarks` also leaves the perimeter — it is packable,
publishable and in no solution, and `IsPackable` is not a statement of intent to publish.

### What CIReleaseBuild becomes

It stops being a packaging mechanism and becomes a compatibility validation: a check that the
working tree compiles against the published catalogue. Two modules fail it today —
`MicroKit.MediatR` (`IUnitOfWork.DiscardChanges` absent from `Persistence.Abstractions
1.0.0-preview.3`) and `MicroKit.Messaging` (`IDomainEventsSink` absent from `MicroKit.MediatR
1.0.0-preview.2`, plus MediatR's own failure inherited through `/deps/`). Both are the same shape:
source calling an API newer than the published sibling carries. D4 removes that gap within a
release; between releases it returns by construction, and that is correct — the mode exists to
surface it. A `CIReleaseBuild` failure is information about publishability, not a broken release.

### Migration path

1. `eng/Versions.props` and the `Directory.Build.props` inheritance; delete the nine `version.json`
2. Ownership guard (D6) and the intra-module `ProjectReference` correction (D5)
3. Release manifest format and the closure computation (D4, D7)
4. One manifest-driven release workflow replacing the nine `release-*.yml`
5. Unlisting pass over foreign and accidental publications (D3)
6. `OutboxMessageFactory` to `internal sealed` — precondition of the first tag, see D10
7. Correct `.claude/CLAUDE.md`, which states the NBGV model, eight release workflows, a dependency
   graph contradicted by the `.csproj` files, and two `.github/` files that do not exist

No tag is cut until steps 1–4 land. Until step 7, `versioning-audit.md` is authoritative over
`.claude/CLAUDE.md` on every point where they disagree.

### Not addressed here

Whether the `/deps/` folder convention survives. It exists to make a source restore work and may be
unnecessary once packing is manifest-driven, but that depends on implementation facts this ADR does
not establish.

### Related ADRs

- ADR-GLOBAL-001 — its migration path step 4 proposes deprecation shims; D10 forbids `[Obsolete]`
  members before `2.0.0`, so that step is to be read as a resolution owed after stable, or
  satisfied without an attribute
- `modules/MicroKit.Messaging/...` — ADR-MSG-002 (`internal sealed` for Core implementations),
  ADR-MSG-016 §4 (break policy on `preview.*` packages)
- Evidence: `versioning-audit.md`, session artefact, 2026-09-05
