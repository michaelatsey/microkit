using FluentAssertions;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using Xunit;

namespace MicroKit.Domain.UnitTests.Events;

public class DomainEventsProviderTests
{
    [Fact]
    public void DomainEvents_Initially_ShouldBeEmpty()
    {
        // Arrange & Act
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));

        // Assert
        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DomainEvents_AfterRaisingEvent_ShouldContainEvent()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var domainEvent = new TestDomainEvent("Test Data");

        // Act
        aggregate.RaiseEvent(domainEvent);

        // Assert
        aggregate.DomainEvents.Should().HaveCount(1);
        aggregate.DomainEvents.Should().Contain(domainEvent);
    }

    [Fact]
    public void DomainEvents_AfterRaisingMultipleEvents_ShouldMaintainOrder()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var event1 = new TestDomainEvent("Event 1");
        var event2 = new TestDomainEvent("Event 2");
        var event3 = new TestDomainEvent("Event 3");

        // Act
        aggregate.RaiseEvent(event1);
        aggregate.RaiseEvent(event2);
        aggregate.RaiseEvent(event3);

        // Assert
        aggregate.DomainEvents.Should().HaveCount(3);
        aggregate.DomainEvents.Should().ContainInOrder(event1, event2, event3);
    }

    [Fact]
    public void DrainDomainEvents_WhenEmpty_ShouldReturnEmptyCollection()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));

        // Act
        var events = aggregate.DrainDomainEvents();

        // Assert
        events.Should().BeEmpty();
        events.Should().BeSameAs(Array.Empty<IDomainEvent>());
    }

    [Fact]
    public void DrainDomainEvents_WithEvents_ShouldReturnAllEventsAndClearCollection()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var event1 = new TestDomainEvent("Event 1");
        var event2 = new TestDomainEvent("Event 2");

        aggregate.RaiseEvent(event1);
        aggregate.RaiseEvent(event2);

        // Act
        var events = aggregate.DrainDomainEvents();

        // Assert
        events.Should().HaveCount(2);
        events.Should().ContainInOrder(event1, event2);
        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DrainDomainEvents_AfterDraining_ShouldReturnEmptyCollectionOnSubsequentCalls()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var domainEvent = new TestDomainEvent("Test Data");

        aggregate.RaiseEvent(domainEvent);
        aggregate.DrainDomainEvents(); // First drain

        // Act
        var events = aggregate.DrainDomainEvents(); // Second drain

        // Assert
        events.Should().BeEmpty();
        events.Should().BeSameAs(Array.Empty<IDomainEvent>());
    }

    [Fact]
    public void DrainDomainEvents_ReturnedCollection_ShouldBeImmutable()
    {
        // Arrange
        var aggregate = new TestAggregateRoot(new TestId(Guid.NewGuid()));
        var domainEvent = new TestDomainEvent("Test Data");

        aggregate.RaiseEvent(domainEvent);

        // Act
        var events = aggregate.DrainDomainEvents();

        // Assert
        events.Should().BeAssignableTo<IReadOnlyCollection<IDomainEvent>>();

        // Verify that even if cast to IList, modifications would throw
        if (events is IList<IDomainEvent> list)
        {
            var act = () => list.Add(new TestDomainEvent("Should fail"));
            act.Should().Throw<NotSupportedException>();
        }
    }
}

// Test implementations
public sealed record TestId(Guid Value) : IEntityId
{
    object IEntityId.Value => Value;
}

public sealed record TestDomainEvent(string Data) : DomainEvent;

public class TestAggregateRoot : AggregateRoot<TestId>
{
    public TestAggregateRoot(TestId id) : base(id) { }

    public void RaiseEvent(IDomainEvent domainEvent)
    {
        RaiseDomainEvent(domainEvent);
    }
}