using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

public class AuditableAggregateRootTests
{
    [Fact]
    public void Constructor_ShouldSetCreatedAt()
    {
        // Arrange
        var id = new TestAuditableId(Guid.NewGuid());
        var beforeCreation = DateTimeOffset.UtcNow.AddMilliseconds(-100);

        // Act
        var aggregate = new TestAuditableAggregateRoot(id);
        var afterCreation = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Assert
        aggregate.CreatedAt.ShouldBeInRange(beforeCreation, afterCreation);
        aggregate.UpdatedAt.ShouldBeNull();
        aggregate.CreatedBy.ShouldBeNull();
        aggregate.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void Constructor_ShouldImplementIAuditableEntity()
    {
        // Arrange & Act
        var aggregate = new TestAuditableAggregateRoot(new TestAuditableId(Guid.NewGuid()));

        // Assert
        aggregate.ShouldBeAssignableTo<IAuditableEntity>();
    }

    [Fact]
    public void Constructor_ShouldInheritFromAggregateRoot()
    {
        // Arrange & Act
        var aggregate = new TestAuditableAggregateRoot(new TestAuditableId(Guid.NewGuid()));

        // Assert
        aggregate.ShouldBeAssignableTo<AggregateRoot<TestAuditableId>>();
    }

    [Fact]
    public void Constructor_ShouldSetId()
    {
        // Arrange
        var id = new TestAuditableId(Guid.NewGuid());

        // Act
        var aggregate = new TestAuditableAggregateRoot(id);

        // Assert
        aggregate.Id.ShouldBe(id);
    }

    [Fact]
    public void Constructor_ShouldMaintainDomainEventFunctionality()
    {
        // Arrange
        var id = new TestAuditableId(Guid.NewGuid());
        var aggregate = new TestAuditableAggregateRoot(id);

        // Act
        aggregate.TestRaiseEvent(new TestDomainEvent("Test"));

        // Assert
        aggregate.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void SetUpdatedInfo_ShouldUpdateAuditFields()
    {
        // Arrange
        var id = new TestAuditableId(Guid.NewGuid());
        var aggregate = new TestAuditableAggregateRoot(id);
        var beforeUpdate = DateTimeOffset.UtcNow;

        // Act
        aggregate.SetUpdated("user123");
        var afterUpdate = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Assert
        aggregate.UpdatedAt.ShouldNotBeNull();
        aggregate.UpdatedAt!.Value.ShouldBeInRange(beforeUpdate, afterUpdate);
        aggregate.UpdatedBy.ShouldBe("user123");
    }

    [Fact]
    public void SetCreatedBy_ShouldSetCreatedByField()
    {
        // Arrange
        var id = new TestAuditableId(Guid.NewGuid());

        // Act
        var aggregate = new TestAuditableAggregateRoot(id, "user456");

        // Assert
        aggregate.CreatedBy.ShouldBe("user456");
    }
}

// Test implementations
public sealed record TestAuditableId(Guid Value) : IEntityId
{
    object IEntityId.Value => Value;
}

public sealed record TestDomainEvent(string Data) : DomainEvent
{
    public string Data { get; } = Data;
}

public class TestAuditableAggregateRoot : AuditableAggregateRoot<TestAuditableId>
{
    public TestAuditableAggregateRoot(TestAuditableId id) : base(id) { }

    internal TestAuditableAggregateRoot(TestAuditableId id, string createdBy) : base(id)
    {
        CreatedBy = createdBy;
    }

    public void TestRaiseEvent(IDomainEvent domainEvent)
    {
        RaiseDomainEvent(domainEvent);
    }

    public void SetUpdated(string updatedBy)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }
}
