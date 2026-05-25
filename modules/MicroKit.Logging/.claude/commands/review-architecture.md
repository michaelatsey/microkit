# /review-architecture

Invoke the `architect` agent to review the current module state against architecture rules.

## Usage

```
/review-architecture [--scope <project>]
```

**Examples:**
```
/review-architecture
/review-architecture --scope Abstractions
/review-architecture --scope OpenTelemetry
```

## What This Command Does

Delegates to the `architect` agent with the full context loaded.

## Steps

```
1. Load .claude/rules/architecture.md
2. Load .claude/rules/dependencies.md
3. Load .claude-context/context/dependency-graph.md
4. If --scope provided: focus on that project only
5. Use agent architect to:
   - Validate project reference graph
   - Check for forbidden dependencies
   - Identify abstraction placement issues
   - Flag any public API in wrong project
6. Output structured report with PASS/FAIL per rule
7. If violations found: propose remediation steps
```
