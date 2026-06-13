---
description: Run the microkit-messaging-implementer agent to produce an implementation plan before writing any code. Always use this command first.
---

Use the microkit-messaging-implementer agent.

Load in order:
1. `.claude/CLAUDE.md`
2. `.claude/rules/microkit-messaging-architecture.md`
3. `.claude/rules/microkit-messaging-naming.md`
4. `.claude/rules/microkit-messaging-dependencies.md`
5. `.claude/rules/microkit-messaging-testing.md`
6. `.claude/rules/microkit-messaging-outbox-inbox.md`
7. `.claude-context/context/microkit-messaging-architectural-decisions.md` (if present)

Then produce a complete implementation plan for: $ARGUMENTS

Do not write any code yet. Produce the plan only.
Do not commit anything.
