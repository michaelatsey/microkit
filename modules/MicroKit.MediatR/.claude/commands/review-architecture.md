# /review-architecture

Invoke the `architect` agent to review the current module state against the CQRS and
dependency rules.

## Usage

```
/review-architecture [--scope <project>]
```

**Examples:**
```
/review-architecture
/review-architecture --scope Abstractions
/review-architecture --scope Behaviors
```

## What This Command Does

Delegates to the `architect` agent with the full context loaded.

## Steps

```
1. Load .claude/rules/cqrs-patterns.md
2. Load .claude/rules/dependencies.md
3. Load .claude/rules/no-handler-coupling.md
4. Load .claude-context/context/dependency-graph.md
5. Load .claude-context/standards/handler-contracts.md
6. If --scope provided: focus on that project only
7. Use agent architect to:
   - Validate the 4-project reference graph
   - Check for forbidden dependencies (FluentValidation/Polly outside Behaviors, NSubstitute outside Testing)
   - Confirm Abstractions purity (no MediatR engine, only MediatR.Contracts)
   - Verify CQRS separation (no handler implements both ICommandHandler and IQueryHandler)
   - Confirm no IMediator injected into handlers
   - Check PipelineOrder values are unchanged and unique
8. Output structured report with PASS/FAIL per rule
9. If violations found: propose remediation steps (and an ADR if the decision has ecosystem impact)
```
