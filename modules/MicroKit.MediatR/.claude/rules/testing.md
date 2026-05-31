# Rule: Testing — MicroKit.MediatR

## Test Project Responsibilities

| Project | Tests |
|---------|-------|
| `UnitTests` | Handlers, behaviors, dispatch logic — isolated, no I/O, no real `IMediator` |
| `IntegrationTests` | Full pipeline via DI, behavior ordering, real `IMediator` wiring |
| `ArchitectureTests` | Dependency & CQRS rules via NetArchTest |
| `PerformanceTests` | Dispatch latency / allocation regression via BenchmarkDotNet |

## Library Choices

- **xUnit** — test framework (no NUnit, no MSTest)
- **Shouldly** — all assertions (`result.ShouldBe(...)`, `result.IsSuccess.ShouldBeTrue()`) — see root `.claude/rules/testing-libraries.md`
- **FluentAssertions is banned** — commercial license (Xceed EULA) in v8+. Any `.Should()` call or `using FluentAssertions;` blocks the build.
- **NSubstitute** — all mocks and stubs (no Moq, no manual fakes)
- **MicroKit.MediatR.Testing** — the harness package (`CommandHandlerTestHarness`, `QueryHandlerTestHarness`, `BehaviorTestHarness`, `DomainEventTestHarness`)
- **NetArchTest.Rules** — architecture tests only

## Isolation Principle (no real MediatR for unit tests)

Every handler must be testable without a DI container, without `WebApplicationFactory`, and
without a real `IMediator`:

```csharp
var harness = new CommandHandlerTestHarness<CreateOrderCommand, Result<OrderId>>(
    new CreateOrderHandler(mockRepo, mockEvents));

var result = await harness.SendAsync(new CreateOrderCommand(userId, items));

result.IsSuccess.ShouldBeTrue();
harness.AssertEventPublished<OrderCreatedEvent>();
```

If a handler cannot be tested this way, its dependencies are too coupled → refactor (see `.claude/rules/no-handler-coupling.md`).

## xUnit Conventions

- Test classes: `sealed` — no inheritance in test classes
- Test method naming: `Method_Scenario_ExpectedResult` — `Handle_WhenUserNotFound_ReturnsFailure`
- `[Fact]` for deterministic tests, `[Theory]` + `[InlineData]` for parameterized
- `[Collection]` for tests sharing expensive fixtures (full-pipeline integration)
- No `Thread.Sleep` — use `Task.Delay` with `CancellationToken` if timing is required

## Mandatory Cases per Handler

- [ ] Happy path (success)
- [ ] Not found / entity absent
- [ ] Validation failure (if applicable)
- [ ] Cancellation via `CancellationToken`
- [ ] Domain event published (command handlers)
- [ ] No side-effect on failure

## Mandatory Cases per Behavior

- [ ] `Handle_WhenMarkerPresent_AppliesLogic`
- [ ] `Handle_WhenMarkerAbsent_PassesThrough`
- [ ] `Handle_WhenShortCircuit_DoesNotCallNext` (if the behavior can short-circuit)
- [ ] `Handle_WhenError_PropagatesCorrectly`

## Test Project .csproj

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```

These two properties are mandatory in every test project — no XML docs required, and the
`Method_Scenario_ExpectedResult` convention uses underscores (CA1707).

## Detecting Violations

```bash
# FluentAssertions must never appear
grep -rn 'FluentAssertions\|\.Should()\.' modules/MicroKit.MediatR/tests/
```
