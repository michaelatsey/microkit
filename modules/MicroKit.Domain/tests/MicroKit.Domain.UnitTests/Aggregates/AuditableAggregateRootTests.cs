using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Events;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.Aggregates;

// What is specific to the aggregate base. The audit rules both bases share run in AuditableBasesTests.
public sealed class AuditableAggregateRootTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Creator = new("user-123");

    [Fact]
    public void Constructor_ShouldImplementIAuditableEntity()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);

        aggregate.ShouldBeAssignableTo<IAuditableEntity>();
        aggregate.ShouldBeAssignableTo<AggregateRoot<TestAuditableId>>();
    }

    [Fact]
    public void Constructor_ShouldSetId()
    {
        var id = NewId();

        new TestAuditableAggregateRoot(id, CreatedAt, Creator).Id.ShouldBe(id);
    }

    [Fact]
    public void RaiseDomainEvent_StoresTheEventAsSupplied()
    {
        var aggregate = new TestAuditableAggregateRoot(NewId(), CreatedAt, Creator);
        var domainEvent = new TestAuditableEvent("Test", CreatedAt);

        aggregate.TestRaiseEvent(domainEvent);

        aggregate.DomainEvents.ShouldBe([domainEvent]);
        aggregate.DomainEvents[0].OccurredAt.ShouldBe(CreatedAt);
    }

    private static TestAuditableId NewId() => new(Guid.CreateVersion7());
}

// Test implementations
public sealed record TestAuditableId(Guid Value) : IEntityId;

public sealed record TestAuditableEvent(string Data, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed class TestAuditableAggregateRoot : AuditableAggregateRoot<TestAuditableId>
{
    public TestAuditableAggregateRoot(TestAuditableId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy) { }

    public void TestRaiseEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}
