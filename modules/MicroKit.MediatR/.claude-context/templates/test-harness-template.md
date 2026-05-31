# Template: Test Harness

Code template for xUnit test classes using the `MicroKit.MediatR.Testing` harnesses.
Used by `/new-handler-tests`. **Shouldly + NSubstitute only — FluentAssertions is banned.**

---

## File: `{Handler}Tests.cs` — Command Handler

```csharp
using MicroKit.MediatR.Abstractions;
using MicroKit.MediatR.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.{Feature};

public sealed class {Verb}{Entity}HandlerTests
{
    private readonly I{Entity}Repository _repo = Substitute.For<I{Entity}Repository>();
    private readonly IDomainEventDispatcher _events = Substitute.For<IDomainEventDispatcher>();
    private readonly CommandHandlerTestHarness<{Verb}{Entity}Command, Result<{ResultType}>> _harness;

    public {Verb}{Entity}HandlerTests()
        => _harness = new(new {Verb}{Entity}Handler(_repo, _events));

    [Fact]
    public async Task Handle_WhenValid_ReturnsSuccess()
    {
        var expected = /* ... */;
        _repo.SaveAsync(Arg.Any<{Entity}>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _harness.SendAsync(new {Verb}{Entity}Command(/* ... */));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Fact]
    public async Task Handle_WhenNotFound_ReturnsFailure()
    {
        _repo.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(({Entity}?)null);

        var result = await _harness.SendAsync(new {Verb}{Entity}Command(/* ... */));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<{Entity}NotFoundError>();
    }

    [Fact]
    public async Task Handle_WhenSuccessful_PublishesEvent()
    {
        await _harness.SendAsync(new {Verb}{Entity}Command(/* ... */));
        _harness.AssertEventPublished<{Entity}{FactPast}Event>();
    }

    [Fact]
    public async Task Handle_WhenFails_PublishesNoEvent()
    {
        await _harness.SendAsync(new {Verb}{Entity}Command(/* invalid */));
        _harness.AssertNoEventsPublished();
    }

    [Fact]
    public async Task Handle_WhenCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _harness.SendAsync(new {Verb}{Entity}Command(/* ... */), cts.Token));
    }
}
```

## Query Handler

```csharp
private readonly QueryHandlerTestHarness<Get{Entity}Query, Result<{Dto}>> _harness
    = new(new Get{Entity}Handler(_readRepo));

[Fact]
public async Task Handle_WhenExists_ReturnsDto()
{
    _readRepo.FindAsync(id, Arg.Any<CancellationToken>()).Returns(entity);
    var result = await _harness.QueryAsync(new Get{Entity}Query(id));
    result.IsSuccess.ShouldBeTrue();
    result.Value.Id.ShouldBe(id);
}
```

## Domain Event Handler

```csharp
private readonly DomainEventTestHarness<{Entity}{FactPast}Event, {Entity}{FactPast}Notification> _harness
    = new(new {Action}Handler(_service));

[Fact]
public async Task Handle_When{Fact}_Performs{Action}()
{
    var notification = new {Entity}{FactPast}Notification(new {Entity}{FactPast}Event(/* ... */));
    await _harness.HandleAsync(notification);
    await _service.Received(1).{Method}(Arg.Any<...>(), Arg.Any<CancellationToken>());
}
```

## Behavior

```csharp
public sealed class {Name}BehaviorTests
{
    [Fact]
    public async Task Handle_WhenMarkerAbsent_PassesThrough()
    {
        var behavior = new {Name}Behavior<PlainRequest, Result<Unit>>(/* deps */);
        var next = Substitute.For<RequestHandlerDelegate<Result<Unit>>>();
        next().Returns(Result.Success(Unit.Value));

        await behavior.Handle(new PlainRequest(), next, CancellationToken.None);

        await next.Received(1)();
    }

    [Fact]
    public async Task Handle_WhenShortCircuit_DoesNotCallNext()
    {
        // Arrange a short-circuit condition; assert:
        // await next.DidNotReceive()();
    }
}
```

## Rules

- `sealed` test classes; `Method_Scenario_ExpectedResult` naming
- **Shouldly** assertions only (`ShouldBe`, `ShouldBeTrue`, `ShouldBeOfType`, `Should.ThrowAsync`)
- **NSubstitute** for every dependency
- No real `IMediator`, no DI container, no database for unit tests
- Test project `.csproj`: `<GenerateDocumentationFile>false</GenerateDocumentationFile>` + `<NoWarn>CS1591;CA1707</NoWarn>`
