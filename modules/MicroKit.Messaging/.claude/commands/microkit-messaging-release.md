---
description: Prepare and validate a MicroKit.Messaging release. Runs the full pre-release checklist and produces git tag commands for the human to execute.
---

Use the microkit-messaging-release-manager agent.

Load in order:
1. `.claude/CLAUDE.md`
2. `version.json`

Run the full pre-release checklist for version: $ARGUMENTS

Produce:
1. Checklist results (PASS/FAIL per item)
2. Blocking issues if any
3. Exact git commands for the human to execute (tag + push + back-merge)

Do not execute any git commands. Do not commit anything.
