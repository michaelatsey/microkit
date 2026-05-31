# Workflow: Testing Handlers

How to write a complete, isolated test suite for a handler or behavior.

## When to Run

After adding or changing any handler, behavior, or domain event handler — before merge.

## Principle

Test in isolation with the `MicroKit.MediatR.Testing` harnesses: NSubstitute mocks, Shouldly
assertions, no real `IMediator`, no DI container, no database. See
`.claude/skills/handler-testing/SKILL.md`.

## Steps

### 1. Generate the Skeleton

```
/new-handler-tests <HandlerOrBehaviorName> --type <command|query|event|stream|behavior>
```

This invokes the `handler-test-generator` agent (Shouldly + NSubstitute).

### 2. Pick the Right Harness

| Subject | Harness |
|---------|---------|
| Command handler | `CommandHandlerTestHarness<TCommand, TResult>` (`SendAsync`) |
| Query handler | `QueryHandlerTestHarness<TQuery, TResult>` (`QueryAsync`) |
| Domain event handler | `DomainEventTestHarness<TEvent, TNotification>` (`HandleAsync`) |
| Behavior | `BehaviorTestHarness` / direct `Handle(request, next, ct)` |

### 3. Cover the Mandatory Matrix

Handlers:
- [ ] Happy path
- [ ] Not found / entity absent
- [ ] Validation failure (if applicable)
- [ ] Cancellation via `CancellationToken`
- [ ] Domain event published (commands) — `AssertEventPublished<T>()`
- [ ] No side-effect on failure — `AssertNoEventsPublished()`

Behaviors:
- [ ] `Handle_WhenMarkerPresent_AppliesLogic`
- [ ] `Handle_WhenMarkerAbsent_PassesThrough`
- [ ] `Handle_WhenShortCircuit_DoesNotCallNext` (if applicable)
- [ ] `Handle_WhenError_PropagatesCorrectly`

### 4. Assert with Shouldly Only

```csharp
result.IsSuccess.ShouldBeTrue();
result.Value.ShouldBe(expected);
result.Error.ShouldBeOfType<UserNotFoundError>();
```

Never `.Should()` (FluentAssertions banned), never `Assert.Equal`.

### 5. Run

```bash
dotnet test modules/MicroKit.MediatR/tests/MicroKit.MediatR.UnitTests/ --no-build \
  --filter "ClassName=<HandlerName>Tests"
```

### 6. Full Pipeline (integration)

For ordering/wiring concerns, add an integration test that builds the real pipeline via DI and
asserts a behavior ran before the handler (e.g., validation blocks an invalid command).
