# Claude Code Configuration Audit Report

**Date:** 2026-05-24
**Scope:** Full `.claude/` directory audit across monorepo (root + 3 active modules)
**Reference:** https://code.claude.com/docs/en/settings, https://code.claude.com/docs/en/hooks, https://code.claude.com/docs/en/sub-agents

---

## 1. Files Modified

### settings.json (4 files)

| File | Changes |
|------|---------|
| `.claude/settings.json` | Rewrote: removed 9 invalid keys, kept `model`/`permissions`/`hooks`, added `$schema`, added `permissions.deny` for git push to main |
| `modules/MicroKit.Domain/.claude/settings.json` | Rewrote: removed 10 invalid keys, fixed `model` from object to string, added `PostToolUse` Write/Edit hook for build verification |
| `modules/MicroKit.MediatR/.claude/settings.json` | Rewrote: removed 12 invalid keys, fixed `model` from object to string, added `PostToolUse` Write/Edit hook for build verification |
| `modules/MicroKit.Result/.claude/settings.json` | Rewrote: removed 10 invalid keys, fixed `model` from object to string, added `PostToolUse` Write/Edit hook for build verification |

### Agent files (9 files)

| File | Changes |
|------|---------|
| `.claude/agents/monorepo-orchestrator.md` | Added YAML frontmatter (name, description, model, tools), restructured body as system prompt |
| `.claude/agents/release-manager.md` | Added YAML frontmatter (name, description, model, tools), restructured body as system prompt |
| `modules/MicroKit.Domain/.claude/agents/domain-architect.md` | Added YAML frontmatter |
| `modules/MicroKit.Domain/.claude/agents/domain-reviewer.md` | Added YAML frontmatter |
| `modules/MicroKit.Domain/.claude/agents/domain-test-generator.md` | Added YAML frontmatter with Write/Edit tools |
| `modules/MicroKit.MediatR/.claude/agents/mediatr-architect.md` | Added YAML frontmatter |
| `modules/MicroKit.MediatR/.claude/agents/behavior-designer.md` | Added YAML frontmatter with Write/Edit tools |
| `modules/MicroKit.MediatR/.claude/agents/handler-test-generator.md` | Added YAML frontmatter with Write/Edit tools |
| `modules/MicroKit.Result/.claude/agents/result-architect.md` | Added YAML frontmatter |
| `modules/MicroKit.Result/.claude/agents/dotnet-reviewer.md` | Added YAML frontmatter |
| `modules/MicroKit.Result/.claude/agents/test-generator.md` | Added YAML frontmatter with Write/Edit tools |

---

## 2. Keys Moved from settings.json to CLAUDE.md

All removed keys already had equivalent content in their respective CLAUDE.md files. No CLAUDE.md modifications were needed.

### Root `.claude/settings.json`

| Removed Key | Destination | Notes |
|-------------|-------------|-------|
| `version` | N/A | Not a valid Claude Code setting, no equivalent needed |
| `project` | Already in `.claude/CLAUDE.md` | Project metadata section |
| `behavior.autoApprove` | Converted to `permissions.allow` | Mapped to valid tool patterns |
| `behavior.requireApproval` | Partially in `permissions.deny` | git push to main blocked via deny + PreToolUse hook |
| `behavior.neverDo` | Already in `.claude/CLAUDE.md` | Listed as conventions/rules |
| `moduleRegistry` | Already in `.claude/CLAUDE.md` | Module registry table |
| `navigation` | Already in `.claude/CLAUDE.md` | Navigation rules section |
| `agentDefaults` | Already in `.claude/CLAUDE.md` | Agents auto-load rules via frontmatter |
| `ci` | Already in `.claude/CLAUDE.md` | CI/CD section |

### Module settings.json files (Domain, MediatR, Result)

| Removed Key | Destination |
|-------------|-------------|
| `version` | N/A |
| `project` | Already in module CLAUDE.md |
| `model.preferred`/`model.fallback` | Replaced with `model` string |
| `behavior` | Already in module CLAUDE.md rules |
| `codeStyle` | Already in module `.claude/rules/csharp-style.md` |
| `testing` | Already in module CLAUDE.md |
| `documentation` | Already in module CLAUDE.md |
| `agentDefaults` | Agents now use frontmatter fields |
| `subAgents` | Agents now discovered automatically from `.claude/agents/` |
| `hooks` (invalid events) | Removed entirely — see section 3 |
| `pipelineDefaults` (MediatR) | Already in CLAUDE.md pipeline section |

---

## 3. Invalid Hooks Removed

### Root `.claude/settings.json`
- None removed. `SessionStart` and `PreToolUse` are valid events with correct format.

### `modules/MicroKit.Domain/.claude/settings.json`
| Removed Hook Event | Reason |
|---|---|
| `postGenerateType` | Not a valid Claude Code hook event |
| `postGenerateEvent` | Not a valid Claude Code hook event |

### `modules/MicroKit.MediatR/.claude/settings.json`
| Removed Hook Event | Reason |
|---|---|
| `preTask` | Not a valid Claude Code hook event |
| `postGenerateHandler` | Not a valid Claude Code hook event |
| `postGenerateBehavior` | Not a valid Claude Code hook event |
| `onError` | Not a valid Claude Code hook event |

### `modules/MicroKit.Result/.claude/settings.json`
| Removed Hook Event | Reason |
|---|---|
| `preTask` | Not a valid Claude Code hook event |
| `postTask` | Not a valid Claude Code hook event |
| `onError` | Not a valid Claude Code hook event |

**Note:** The `.claude/hooks/*.md` files remain as contextual documentation referenced by CLAUDE.md and rules. They are NOT executable hooks — they are guidelines for Claude to follow, not shell commands.

---

## 4. Agents Created or Updated

### Updated with proper YAML frontmatter (11 agents)

All agents were updated from plain Markdown (no frontmatter) to the official subagent format with YAML frontmatter.

| Agent | `name` | `model` | `tools` |
|-------|--------|---------|---------|
| monorepo-orchestrator | `monorepo-orchestrator` | inherit | Read, Grep, Glob, Bash, Agent |
| release-manager | `release-manager` | inherit | Read, Grep, Glob, Bash |
| domain-architect | `domain-architect` | inherit | Read, Grep, Glob |
| domain-reviewer | `domain-reviewer` | inherit | Read, Grep, Glob |
| domain-test-generator | `domain-test-generator` | inherit | Read, Grep, Glob, Write, Edit |
| mediatr-architect | `mediatr-architect` | inherit | Read, Grep, Glob |
| behavior-designer | `behavior-designer` | inherit | Read, Grep, Glob, Write, Edit |
| handler-test-generator | `handler-test-generator` | inherit | Read, Grep, Glob, Write, Edit |
| result-architect | `result-architect` | inherit | Read, Grep, Glob |
| dotnet-reviewer | `dotnet-reviewer` | inherit | Read, Grep, Glob |
| test-generator | `test-generator` | inherit | Read, Grep, Glob, Write, Edit |

---

## 5. Issues Found and Fixed

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | CRITICAL | `model` field in 3 module settings.json was an object `{"preferred": "...", "fallback": "..."}` instead of a string | Changed to `"model": "claude-sonnet-4-6"` |
| 2 | CRITICAL | 9 invalid top-level keys in root settings.json (`version`, `project`, `behavior`, `moduleRegistry`, `navigation`, `agentDefaults`, `ci`) | Removed; content already in CLAUDE.md |
| 3 | CRITICAL | 10-12 invalid keys per module settings.json (`version`, `project`, `behavior`, `codeStyle`, `testing`, `documentation`, `agentDefaults`, `subAgents`, `hooks`, `pipelineDefaults`) | Removed; content already in module CLAUDE.md |
| 4 | HIGH | Invalid hook events in all module settings.json (`postGenerateType`, `postGenerateEvent`, `preTask`, `postTask`, `postGenerateHandler`, `postGenerateBehavior`, `onError`) — these are NOT valid Claude Code hook events | Removed. Replaced with valid `PostToolUse` hook for build verification |
| 5 | HIGH | All 11 agent files lacked YAML frontmatter — Claude Code cannot discover name, description, model, or tools without frontmatter | Added complete frontmatter to all agents |
| 6 | MEDIUM | `behavior.autoApprove` in root settings.json used free-text descriptions instead of valid permission rule patterns | Converted to `permissions.allow` with correct `Bash(...)` patterns |
| 7 | MEDIUM | No `$schema` reference in any settings.json | Added `"$schema": "https://json.schemastore.org/claude-code-settings.json"` to all 4 files |
| 8 | MEDIUM | No `permissions.deny` rule to block direct push to main | Added `Bash(git push * main)` to deny rules in root settings.json |
| 9 | LOW | No `PostToolUse` build verification hooks in module settings.json | Added `PostToolUse` Write|Edit hooks that run `dotnet build` on file changes |

---

## 6. Issues Found But Requiring Manual Review

| # | Issue | Action Required |
|---|-------|-----------------|
| 1 | **Command .md files lack YAML frontmatter** — 13 command files in `.claude/commands/` and module `commands/` directories work as-is (backwards compatible) but would benefit from frontmatter (`name`, `description`, `disable-model-invocation: true`) for proper skill integration | Add frontmatter to each command file to enable auto-discovery and argument hints |
| 2 | **Hook .md files are documentation, not executable hooks** — 7 files in `.claude/hooks/` and module `hooks/` directories are guidelines, not shell commands. They should be referenced in CLAUDE.md rules, not in settings.json | Verify they are referenced in the appropriate CLAUDE.md or rules files |
| 3 | **Skill .md files format** — 9 skill files exist but their format hasn't been audited for SKILL.md compliance. Per official docs, skills should live in `.claude/skills/<name>/SKILL.md` directories, not as flat `.md` files | Consider migrating to the `skills/<name>/SKILL.md` directory structure |
| 4 | **2 pre-existing test failures** — `MoneyTests.ToString_ShouldReturnFormattedString` and `PercentageTests.ToString_ShouldReturnFormattedString` fail due to locale formatting (comma vs dot decimal separator). Unrelated to this audit | Fix tests to use `CultureInfo.InvariantCulture` in `ToString()` implementations |
| 5 | **settings.local.json allows `Bash(git push *)`** which is broader than the project deny rule for `Bash(git push * main)` — the local allow may override the project deny depending on settings merge behavior | Review whether `settings.local.json` permissions should be tightened |

---

## 7. Recommendations for Next Steps

1. **Migrate commands to skills directory structure** — Move `.claude/commands/*.md` to `.claude/skills/<name>/SKILL.md` with proper frontmatter for better discoverability and supporting files.

2. **Add frontmatter to all command files** — At minimum, add `description` and `disable-model-invocation: true` to commands that should only be user-invoked (e.g., `/new-module`, `/release`).

3. **Fix locale-dependent tests** — The 2 failing tests use culture-dependent decimal formatting. Fix by using `CultureInfo.InvariantCulture` in `ToString()` overrides or by making tests culture-agnostic.

4. **Consider `PostToolUse` hook async mode** — The build verification hooks added to module settings run synchronously after every Write/Edit. If this causes latency during development, set `"async": true` to run builds in the background.

5. **Add `permissions.deny` for `Bash(git tag *)` in root settings.json** — The original `behavior.requireApproval` listed `git tag` as requiring approval. Consider adding `"Bash(git tag *)"` to the `permissions.ask` list.

6. **Audit remaining planned modules** — When MicroKit.Messaging, Persistence, Caching, etc. are bootstrapped, ensure their `.claude/settings.json` follow the corrected format from this audit.

---

## Validation Results

| Check | Result |
|-------|--------|
| `dotnet build modules/MicroKit.Domain` | 0 warnings, 0 errors |
| `dotnet test modules/MicroKit.Domain` | 146/148 pass (2 pre-existing locale failures) |
| settings.json schema validation | All 4 files use only documented keys |
| Agent frontmatter | All 11 agents have valid YAML frontmatter |
| Hook events | All hook events in settings.json are valid Claude Code events |
| Model format | All `model` fields are strings, not objects |
