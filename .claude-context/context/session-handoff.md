# Convention — Session handoff

Status: convention. Durable. Changes only when the method itself changes.

Two agents work on this repository and they do not share memory:

- **The web session** decides. Design, ADR confrontation, arbitration. No code.
- **Claude Code (WSL2)** executes. Plan, implementation, agents, tests. No design decision.

Neither can read the other's context. Everything that must cross does so through a
file. This document defines which file, in which direction, carrying what.

---

## 1. Authority map

One fact lives in exactly one artifact. If two artifacts state it, one of them is
already wrong.

| Artifact | Owns | Rhythm |
|---|---|---|
| `CLAUDE.md` | Vision, architecture rules, dependency graph | Rare |
| `microkit-architectural-decisions.md` / ADRs | Decisions, with their reasoning | On decision |
| `.claude-context/sessions/NNN-*.md` | **Current state.** What shipped, what is owed | Every lot |
| `session-handoff.md` (this file) | The method | Once |
| `LOT.md` | One lot's passing order, web → Claude Code | Every web session, overwritten |

**The session trace is the sole authority on current state.** Not `CLAUDE.md`, not any
status table. A status table is written once and rots; a trace is produced by
construction at the end of every lot, so it is always fresh.

---

## 2. The cycle

1. **Web session opens.** The most recent trace is in the Project. Design, confront the
   ADRs, arbitrate. Nothing is implemented here.
2. **Web session closes.** Produce `LOT.md` — English, repo root, gitignored.
3. **WSL2.** Launch Claude Code from the module directory (required for module-scoped
   agents). Opening prompt: `Read LOT.md, then /plan.` Nothing else.
4. **Lot runs** under the immutable flow: plan → architect review → implementation →
   post-code agents in separate sessions → merge only after approval.
5. **Lot closes.** Claude Code writes the trace into `.claude-context/sessions/`
   (`/sd-session-trace` when the `solution-design` plugin is available).
   Commit, PR and merge are manual.
6. **Loop closes.** PR merged → the trace is uploaded to the Project immediately.

Step 6 is the one that fails silently. A trace that exists in the repo but not in the
Project leaves the next web session designing against a stale picture — and confident
about it, which is worse than ignorant.

---

## 3. `LOT.md` — what it is

A passing order for **one lot**. Ephemeral, overwritten each time, never committed.

It carries no durable decision. If something in a `LOT.md` deserves to survive the
lot, it belongs in an ADR or in the trace, and the handoff only points at it.

Root of the repo, not `/tmp`: a lot can span several days and `/tmp` does not survive a
WSL restart. `/tmp` stays reserved for the secret file-drop, which must disappear.

### Template

```markdown
# LOT — <lot name>

Reads with: <most recent session trace>. That document is authoritative on state.

## Objective
<One lot. One sentence. What is done when this is done.>

## Frozen — do not reopen
<Decisions already arbitrated that the lot must not revisit. Name them, do not
re-argue them. Point at the ADR or the trace section that holds the reasoning.>

## Observed state
<What is merged, what is implemented but unmerged, what is broken. Facts with
PR numbers or branch names, not impressions.>

## Owed, in order
1. <...>
2. <...>

## Read first
<Explicit ordered file list. Include the agent file when an agent is involved.>

## Out of scope — refuse and flag
<What the lot must not touch. Anything drifting beyond this is reported, not done.>

## Constraints
Working agreement: see `session-handoff.md` §5. Do not restate it here.
```

---

## 4. What must never appear where

| Artifact | Must not contain |
|---|---|
| `LOT.md` | Durable decisions · rules copied from elsewhere · state not verified |
| Session trace | The method itself · rules already in `CLAUDE.md` |
| `CLAUDE.md` | Status tables · published versions · dated decisions · current state |
| ADR | Execution plan · ordering · prompts |

The failure mode is duplication, not omission. A rule stated in three places has three
update rhythms and no owner, so the stalest copy wins whenever it is read first.

---

## 5. Working agreement — stated once, here

Referenced by every handoff, copied into none.

- One step at a time. Plan first, validate or amend, then implement.
- No unrequested analysis. Answer what is asked.
- Post-code agents in separate sessions, every prompt carrying "Do not commit anything":
  - `api-reviewer` when the public surface changes
  - `dependency-guardian` when a `.csproj` moves
  - `distributed-context-specialist` for `AsyncLocal`, scoping, workers
- **Claude Code never touches git.** All git is manual.
- Branch from `dev`, squash merge, `--delete-branch` on feature branches only — never
  when the head is `dev`.
- PR body via `--body-file /tmp/pr-<slug>.md`, then `rm -f /tmp/pr-<slug>.md` — that file
  only, never `rm /tmp/*`.
- Post-review corrections go on a new branch and a new PR, never onto the branch of a PR
  already merged.
- Shouldly, NSubstitute, xUnit. FluentAssertions forbidden. Recording fakes over
  `DidNotReceive()`.
- Testcontainers PostgreSQL for anything touching uniqueness or concurrency.
- Mutation-check anything load-bearing.
- Prompts for Claude Code are always written in English.

---

## 6. The test of the method

- Re-explaining to Claude Code a decision already taken in the web session → the
  `LOT.md` was incomplete.
- Re-explaining to the web session a choice made in WSL2 → the trace was not uploaded.
- Two artifacts disagreeing on a fact → the authority map was violated. Delete the copy,
  do not reconcile it.
