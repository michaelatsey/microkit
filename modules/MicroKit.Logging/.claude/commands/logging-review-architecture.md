# /logging-review-architecture

Invoke the `logging-architect` agent to review the current module state against architecture rules.

## Usage

```
/logging-review-architecture [--scope <project>]
```

**Examples:**
```
/logging-review-architecture
/logging-review-architecture --scope Abstractions
/logging-review-architecture --scope OpenTelemetry
```

## What This Command Does

Delegates to the `logging-architect` agent with the full context loaded.

## Steps

```
1. Load .claude/rules/logging-architecture.md
2. Load .claude/rules/logging-dependencies.md
3. Load .claude-context/context/logging-dependency-graph.md
4. If --scope provided: focus on that project only
5. Use agent logging-architect to:
   - Validate project reference graph
   - Check for forbidden dependencies
   - Identify abstraction placement issues
   - Flag any public API in wrong project
6. Output structured report with PASS/FAIL per rule
7. If violations found: propose remediation steps
```
