---
name: handler-testing
description: How to use the MicroKit.MediatR.Testing harnesses to test handlers and behaviors in isolation — CommandHandlerTestHarness, QueryHandlerTestHarness, DomainEventTestHarness, BehaviorTestHarness. Use whenever writing or reviewing handler/behavior tests. Enforces Shouldly assertions and NSubstitute mocks, no real IMediator, no DI container.
---

# Skill: Testing Handlers & Behaviors

How to use the `MicroKit.MediatR.Testing` harnesses. For the rules, see `.claude/rules/testing.md`;
for ready-to-adapt code, see `.claude-context/templates/test-harness-template.md`. To generate a
full suite, use `/new-handler-tests`.

## The Principle: Isolation Without MediatR

A handler is a plain class. You test it by constructing it with NSubstitute mocks and invoking it
through a harness — no DI container, no `WebApplicationFactory`, no real `IMediator`, no database.
If you can't, the handler is over-coupled (see `.claude/rules/no-handler-coupling.md`).

## Pick the Harness

| Subject | Harness | Entry method |
|---------|---------|--------------|
| Command handler | `CommandHandlerTestHarness<TCommand, TResult>` | `SendAsync` |
| Query handler | `QueryHandlerTestHarness<TQuery, TResult>` | `QueryAsync` |
| Domain event handler | `DomainEventTestHarness<TEvent, TNotification>` | `HandleAsync` |
| Behavior | `BehaviorTestHarness` / direct `Handle(request, next, ct)` | — |

## Command Handler — the Full Shape

```csharp
public sealed class CreateOrderHandlerTests
{
    private readonly IOrderRepository _repo = Substitute.For<IOrderRepository>();
    private readonly IDomainEventDispatcher _events = Substitute.For<IDomainEventDispatcher>();
    private readonly CommandHandlerTestHarness<CreateOrderCommand, Result<OrderId>> _harness;

    public CreateOrderHandlerTests()
        => _harness = new(new CreateOrderHandler(_repo, _events));

    [Fact]
    public async Task Handle_WhenValid_ReturnsSuccessAndPublishesEvent()
    {
        var expected = OrderId.New();
        _repo.SaveAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _harness.SendAsync(new CreateOrderCommand(Guid.NewGuid(), [new("SKU-1", 1)]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
        _harness.AssertEventPublished<OrderCreatedEvent>();
    }
}
```

## Asserting with Shouldly (never FluentAssertions)

```csharp
result.IsSuccess.ShouldBeTrue();
result.Value.ShouldBe(expected);
result.IsFailure.ShouldBeTrue();
result.Error.ShouldBeOfType<UserNotFoundError>();
await _email.Received(1).SendWelcomeAsync("a@b.com", Arg.Any<CancellationToken>());
```

## Behavior Testing: prove the guard and the short-circuit

The two tests every behavior needs:

```csharp
[Fact] public async Task Handle_WhenMarkerAbsent_PassesThrough() { /* next() received once, unchanged */ }
[Fact] public async Task Handle_WhenMarkerPresent_AppliesLogic() { /* behavior effect observed */ }
```

If the behavior can short-circuit, add: `Handle_WhenShortCircuit_DoesNotCallNext` and assert
`await next.DidNotReceive()();`.

## Cancellation

```csharp
using var cts = new CancellationTokenSource();
await cts.CancelAsync();
await Should.ThrowAsync<OperationCanceledException>(
    async () => await _harness.SendAsync(command, cts.Token));
```

## The Mandatory Matrix

Every handler suite covers: happy path · not-found · validation failure (if any) · cancellation ·
event published (commands) · no side-effect on failure. Every behavior suite covers: marker present ·
marker absent · short-circuit (if any) · error propagation.
