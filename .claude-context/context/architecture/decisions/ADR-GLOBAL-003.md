## ADR-GLOBAL-003: Branching model — a single trunk, `main`

**Date:** 2026-09-19
**Status:** Accepted
**Decided by:** Ange-Michaël Atsé
**Phase:** Cross-module

### Context

The repository runs a git-flow variant, described in `.claude/rules/git-workflow.md`: feature branches
merge into `dev`, a release is a pull request from `dev` to `main` followed by a tag, then `main` is
merged back into `dev`. Three observations make this model a poor fit:

1. **Squash merges and a second long-lived branch do not compose.** A squash-merged branch shares no
   commit with `dev`, so `git log dev..<branch>` cannot tell whether a branch has landed.
   `git-workflow.md` works around it by comparing tree hashes. A workaround the process needs in
   order to answer "is this merged?" is a defect of the process.
2. **In this model, `main` carries no information `dev` does not.** ADR-GLOBAL-002 D8 already makes
   a release an explicit event: one `v<semver>` tag on one commit. A second long-lived branch whose
   only role is to receive released states duplicates what the tag records.
3. **Every release costs two extra merges** — `dev → main`, then `main → dev` — each a chance to
   diverge.

Microsoft's Git branching guidance builds a strategy on three ideas: feature branches for every
change, merged into `main` through pull requests, with `main` kept high-quality and up to date.
Release branches extend that flow only when a published version needs servicing. The repository has
one maintainer; pull requests and CI already provide the isolation a `dev` branch would.

### Decision

**D1 — `main` is the only long-lived branch.** Nothing is pushed to it directly: every change reaches
it through a pull request, squash-merged, keeping its history linear. The ruleset protecting `main`
has an empty bypass list, so the rule binds the repository administrator too; disabling the ruleset
is the only escape hatch, and it is visible. `dev` is retired; there are no back-merges.

**D2 — Work happens on short-lived topic branches cut from `main`.** Named
`<type>/<scope>/<issue>-<slug>`, where `<type>` is a Conventional Commits type (`feat`, `fix`,
`docs`, `chore`, `refactor`, `test`, `ci`), `<scope>` is the module scope of the issues convention
(`messaging`, `monorepo`, …), and `<issue>` is the GitHub issue number. Example:
`fix/messaging/124-tenant-row-stall`. This format is a repository convention. A branch lives for one
issue and is deleted on merge; a squash-merged branch is never reused.

**D3 — The pull request title becomes the squash commit subject.** Pull requests are squash-merged,
and the repository is configured so the squash commit message defaults to the PR title and
description. The PR title therefore follows Conventional Commits (`fix(messaging): …`) and the
description carries `Closes #N`. Repository settings: squash merging only (merge commits and rebase
merging disabled), head branches deleted automatically.

**D4 — A release is a tag on `main`.** Per ADR-GLOBAL-002 D8, one annotated `v<semver>` tag on a
commit of `main`. No release branch is created to prepare a release.

**D5 — A release branch exists only to service a published version.** When a fix must ship for a
released version after `main` has moved past it, `release/<major>.<minor>` is cut from that
version's tag. Every change is integrated into `main` first, then cherry-picked onto the release
branch; a release branch is never merged back into `main`. Each release branch is a supported
version to maintain: it is deleted when that version is no longer supported.

### Rejected alternatives

- **Keep git-flow (`dev` + `main`).** Its value is an integration buffer between several
  contributors' work before it reaches the stable branch. With one maintainer and a PR gate, the
  buffer holds nothing, and it costs the problems listed in the context.
- **Keep `dev` as the single trunk under that name.** Equivalent in mechanics, but every tool,
  guide and contributor expects the trunk of a GitHub repository to be `main`.

### Consequences

- `main` is protected by a ruleset: pull request required, linear history, no force push, no
  deletion, empty bypass list.
- Required status checks are deferred, not waived. CI workflows are path-filtered per module, and a
  required check that a filtered workflow never starts blocks the pull request. Path-filtered
  workflows must first be fronted by a check that always reports; that belongs with the release
  workflow (#107).
- The release workflow of ADR-GLOBAL-002 (#107) triggers on a `v*` tag pushed on `main`.
- `.claude/rules/git-workflow.md` contradicts this ADR and is removed by #110, which moves the
  branch and commit rules Claude Code needs into `.claude/CLAUDE.md`. Two live sources of git
  instructions must not coexist.
- `dev` and the stale `feature/messaging/outbox-message-kind` are deleted once their content is on
  `main`; the default branch returns to `main`.

### Related

- ADR-GLOBAL-002 — D8 (tags `v<semver>`, one per release), D10 (stable when the ecosystem is ready)
- `.claude-context/context/issues-convention.md` — scopes and issue numbers used in branch names
- Microsoft Learn, *Git branching guidance* (Azure Repos)
