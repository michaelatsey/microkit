# /new-handler-tests

Generate a complete xUnit test suite for a CQRS handler or behavior, using the
`MicroKit.MediatR.Testing` harnesses, Shouldly assertions, and NSubstitute mocks.

> Replaces the legacy `/gen-handler-tests`.

## Usage

```
/new-handler-tests <HandlerOrBehaviorName> [--type <command|query|event|stream|behavior>] [--project <UnitTests|IntegrationTests>]
```

**Examples:**
```
/new-handler-tests CreateOrderHandler --type command
/new-handler-tests GetUserByIdHandler --type query
/new-handler-tests SendWelcomeEmailHandler --type event
/new-handler-tests ValidationBehavior --type behavior
```

## Steps

```
1. Load .claude/rules/testing.md
2. Load .claude-context/templates/test-harness-template.md
3. Load .claude-context/standards/handler-contracts.md
4. Read the target handler/behavior source file
5. Identify: dependencies (to mock with NSubstitute), success path, failure paths, cancellation, side-effects (events)
6. Use agent handler-test-generator to produce the suite:
   - File: {Name}Tests.cs in the chosen test project
   - Class: sealed
   - Shouldly assertions only (never .Should(), never Assert.Equal)
   - NSubstitute for all dependencies
   - Correct harness: CommandHandlerTestHarness / QueryHandlerTestHarness / DomainEventTestHarness / BehaviorTestHarness
7. Ensure the mandatory case matrix from testing.md is covered
```

## Mandatory Cases

For a handler:
- Happy path, not-found, validation failure (if applicable), cancellation, event published (commands), no side-effect on failure

For a behavior:
- `Handle_WhenMarkerPresent_AppliesLogic`
- `Handle_WhenMarkerAbsent_PassesThrough`
- `Handle_WhenShortCircuit_DoesNotCallNext` (if it can short-circuit)
- `Handle_WhenError_PropagatesCorrectly`

## Constraints

- `[Fact]` for deterministic tests, `[Theory]` + `[InlineData]` for parameterized
- `NSubstitute` for mocking — no Moq, no manual fakes
- `Shouldly` for all assertions — `FluentAssertions` is banned
- No real `IMediator` for unit tests — use the harnesses
- No `Thread.Sleep` — `Task.Delay` + `CancellationToken` if timing is needed
